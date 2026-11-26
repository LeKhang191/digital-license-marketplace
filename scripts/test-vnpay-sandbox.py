#!/usr/bin/env python3

import os
import json
import time
import shlex
import subprocess
import importlib.util
from test_helpers import api_result

from pathlib import Path
from urllib.parse import urlparse, parse_qs
from urllib.error import HTTPError
from urllib.request import urlopen
from decimal import Decimal

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py")
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

lit = t.literal

STOPPED = set()
RESULTS = []

SCENARIOS = {
    "01": ("Successful payment", "01"),
    "02": ("Cancel or abandon payment", "02"),
    "03": ("Payment session expired", "06"),
    "04": ("Network lost after payment", "08"),
    "05": ("RabbitMQ offline", "10"),
    "06": ("License Vault offline", "12"),
    "07": ("Out-of-stock automatic refund", "13"),
    "08": ("Refund to Wallet", "15"),
}

TOKENS = {}
_original_request = t.request


def authenticated_request(method, path, token=None, data=None):
    username = TOKENS.get(token)

    if username:
        token = ACTIVE_TOKENS[username]

    try:
        return _original_request(method, path, token, data)

    except HTTPError as exc:
        if exc.code != 401 or not username:
            raise

        print(f"[AUTH] Re-login required for {username}")

        fresh_token = login(username)

        return _original_request(
            method, path, fresh_token, data
        )


ACTIVE_TOKENS = {}


def login(username):
    token = t.login(username)

    TOKENS[token] = username
    ACTIVE_TOKENS[username] = token

    return token


t.request = authenticated_request


def api(method, path, token=None, data=None):
    return api_result(t.request, method, path, token, data)


def check(name, condition):
    condition = bool(condition)

    RESULTS.append((name, condition))

    print(
        f"[{'PASS' if condition else 'FAIL'}] {name}",
        flush=True
    )

    return condition


def stop(service):
    STOPPED.add(service)

    t.run([
        "docker", "compose", "stop", service
    ])

    print(f"[FAULT] {service} stopped")


def start(service):
    t.run([
        "docker", "compose", "start", service
    ])

    STOPPED.discard(service)

    print(f"[RECOVERY] {service} started")


def restore():
    for service in list(STOPPED):
        try:
            start(service)
        except Exception:
            print(f"[WARNING] Restore {service} manually")


def preflight():
    print("\n===== SANDBOX PREFLIGHT =====")

    runtime = t.run([
        "docker", "compose", "exec", "-T",
        "payment", "sh", "-c",
        '''
        test -n "$VnPay__TmnCode" &&
        test "$VnPay__TmnCode" != TEST0001 &&
        test -n "$VnPay__HashSecret" &&
        echo CONFIGURED
        '''
    ])

    if runtime != "CONFIGURED":
        raise RuntimeError(
            "Official Sandbox credentials required"
        )

    print("[PASS] Sandbox merchant configured")

    products = t.request(
        "GET", "/catalog/products"
    )

    if products is None:
        raise RuntimeError("Catalog API unavailable")

    print("[PASS] Local API Gateway reachable")

    codespace = os.environ.get("CODESPACE_NAME")
    domain = os.environ.get(
        "GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN"
    )

    public_ipn = os.environ.get("SANDBOX_PUBLIC_IPN")

    if not public_ipn and codespace and domain:
        public_ipn = (
            f"https://{codespace}-8000.{domain}"
            "/api/orders/vnpay-ipn"
        )

    if not public_ipn:
        raise RuntimeError(
            "Set SANDBOX_PUBLIC_IPN to your public HTTPS endpoint"
        )

    if urlparse(public_ipn).scheme != "https":
        raise RuntimeError("Public IPN must use HTTPS")

    try:
        with urlopen(public_ipn, timeout=15) as response:
            result = json.load(response)

            if (
                response.status != 200
                or result.get("RspCode") != "97"
            ):
                raise RuntimeError("Unexpected IPN response")

    except Exception:
        raise RuntimeError(
            "Public IPN unavailable. "
            "Check Codespaces port 8000 visibility "
            "and VNPay IPN configuration."
        ) from None

    print("[PASS] Public IPN endpoint reachable")


    print("[INFO] No synthetic IPN will be generated")


def open_payment(url):
    parsed = urlparse(url)

    if (
        parsed.scheme != "https"
        or parsed.hostname != "sandbox.vnpayment.vn"
    ):
        raise RuntimeError(
            "Payment URL is not official VNPay Sandbox"
        )

    print("\n===== OPEN VNPAY =====")

    # Codespaces browser bridge.
    browser = os.environ.get("BROWSER")

    if browser:
        try:
            subprocess.Popen(
                shlex.split(browser) + [url],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL
            )

            print("[INFO] Browser open requested")

        except OSError:
            print("[INFO] Browser bridge unavailable")

    else:
        print("[INFO] Automatic browser bridge unavailable")

    print("\nFallback: Ctrl + Click the URL below:\n")
    print(url, flush=True)


def create_order(product):
    order = t.request(
        "POST",
        "/orders",
        CUSTOMER,
        {
            "productId": product,
            "quantity": 1,
            "paymentMethod": "VNPAY"
        }
    )

    url = order.get("paymentUrl")

    if not url:
        raise RuntimeError("Missing payment URL")

    parsed = urlparse(url)

    if parsed.scheme != "https" or (
        parsed.hostname != "sandbox.vnpayment.vn"
    ):
        raise RuntimeError("Unexpected payment provider")

    params = parse_qs(parsed.query)

    order["paymentRef"] = params["vnp_TxnRef"][0]

    print(f"\nOrder ID: {order['id']}")
    print(f"Amount: {order['totalAmount']} VND")

    return order


def payment_status(order):
    return t.sql("payment_db", f'''
        SELECT "Status"
        FROM payments
        WHERE "OrderId" = {lit(order["id"])};
    ''')


def order_status(order):
    return t.order_status(order["id"])


def assigned(order):
    return t.assigned_count(order["id"])


def wallet_balance():
    result = t.request(
        "GET",
        "/wallet/me",
        CUSTOMER
    )

    return Decimal(str(result["balance"]))


def refund_count(order):
    return int(t.sql("payment_db", f'''
        SELECT count(*)
        FROM refunds r
        JOIN payments p
          ON p."Id" = r."PaymentId"
        WHERE p."OrderId" = {lit(order["id"])}
          AND r."Status" = 'SUCCEEDED';
    '''))


def payment_count(order):
    return int(t.sql("payment_db", f'''
        SELECT count(*)
        FROM payments
        WHERE "OrderId" = {lit(order["id"])};
    '''))


def paid_event_count(order):
    return int(t.sql("order_db", f'''
        SELECT count(*)
        FROM outbox_messages
        WHERE "AggregateId" = {lit(order["id"])}
          AND "RoutingKey" = 'order.paid';
    '''))


def show_state(order):
    print("\n===== CURRENT STATE =====")
    print(f"Payment: {payment_status(order)}")
    print(f"Order: {order_status(order)}")
    print(f"Fulfillment: {t.fulfillment(order['id']) or '-'}")
    print(f"Assigned keys: {assigned(order)}")
    print(f"Refunds: {refund_count(order)}")


def wait_for(fn, timeout=180):
    start = time.monotonic()

    while time.monotonic() - start < timeout:
        if fn():
            return True

        elapsed = int(time.monotonic() - start)

        print(
            f"[WAIT {elapsed:03d}/{timeout}s]",
            flush=True
        )

        time.sleep(3)

    return False


def wait_completed(order, timeout=180):
    def completed():
        payment = payment_status(order)
        status = order_status(order)
        keys = assigned(order)

        print(
            f"[STATE] Payment={payment} | "
            f"Order={status} | Keys={keys}",
            flush=True
        )

        return (
            payment == "SUCCEEDED"
            and status == "COMPLETED"
            and keys == 1
        )

    return wait_for(completed, timeout)


def check_success(order):
    check(
        "Payment and license delivery completed",
        wait_completed(order)
    )

    check(
        "Payment SUCCEEDED",
        payment_status(order) == "SUCCEEDED"
    )

    check(
        "Exactly one payment",
        payment_count(order) == 1
    )

    check(
        "Exactly one order.paid event",
        paid_event_count(order) == 1
    )

    check(
        "Exactly one assigned license",
        assigned(order) == 1
    )


def check_unpaid(order):
    check(
        "Payment not successful at observation time",
        payment_status(order) in ("PENDING", "FAILED")
    )

    check(
        "Order not fulfilled",
        order_status(order) in ("PENDING", "CANCELED")
    )

    check(
        "No license assigned",
        assigned(order) == 0
    )

    check(
        "No order.paid event",
        paid_event_count(order) == 0
    )


def pause(message):
    input(f"\n{message}\nPress Enter to continue...")


def scenario(number):
    name, case = SCENARIOS[number]
    print("\n" + "=" * 55)
    print(f"TC{number}: {name}")
    print("=" * 55)

    stock = 1 if case == "13" else 3

    product = t.create_product(
        f"sandbox-{case}-{time.time_ns()}",
        stock
    )

    order = create_order(product)

    before = (
        wallet_balance()
        if case in ("13", "15")
        else None
    )

    try:
        # Inject faults AFTER creating the payment session.
        if case == "10":
            stop("rabbitmq")

        elif case == "12":
            stop("license-vault")

        elif case == "13":
            t.disable_only_key(product)

            check(
                "Inventory depleted after order creation",
                t.available_count(product) == 0
            )

        open_payment(order["paymentUrl"])

        instructions = {
            "01": "Complete payment using the official test card.",
            "02": "Cancel or close VNPay without completing payment.",
            "06": "Wait until the VNPay payment session expires.",
            "08": "Complete payment, then disconnect before returning if possible.",
            "10": "Complete payment while RabbitMQ is offline.",
            "12": "Complete payment while License Vault is offline.",
            "13": "Complete payment. The product is now out of stock.",
            "15": "Complete payment. Do NOT reveal the license key."
        }

        print("\n===== YOUR ACTION =====")
        print(instructions[case])

        pause("Finish the browser action first.")

        if case == "02":
            print("\nObserving unpaid order for 20 seconds...")

            unexpected_success = wait_for(
                lambda: payment_status(order) == "SUCCEEDED",
                timeout=20
            )

            check(
                "No unexpected successful payment",
                not unexpected_success
            )

            check_unpaid(order)

            print(
                "[INFO] Unpaid transactions may remain PENDING."
            )

        elif case == "06":
            expired = t.sql("payment_db", f'''
                SELECT (
                    "ExpiresAtUtc" <= now()
                )::int
                FROM payments
                WHERE "OrderId" = {lit(order["id"])};
            ''')

            check(
                "Payment session expired",
                expired == "1"
            )

            check_unpaid(order)

            print(
                "[INFO] This checks expiry and lack of fulfillment, "
                "not whether VNPay rejects every late payment attempt."
            )

        elif case in ("10", "12"):
            check(
                "Payment recorded despite downstream outage",
                wait_for(
                    lambda: payment_status(order) == "SUCCEEDED",
                    timeout=120
                )
            )

            check(
                "No license assigned before recovery",
                assigned(order) == 0
            )

            if case == "10":
                pending = t.sql("payment_db", f'''
                    SELECT count(*)
                    FROM outbox_messages o
                    JOIN payments p
                      ON p."Id" = o."AggregateId"
                    WHERE p."OrderId" = {lit(order["id"])}
                      AND o."RoutingKey" = 'payment.succeeded'
                      AND o."PublishedAtUtc" IS NULL;
                ''')

                check(
                    "Payment event retained in outbox",
                    pending == "1"
                )

            restore()

            check_success(order)

        elif case == "13":
            check(
                "Automatic refund completes",
                wait_for(
                    lambda: order_status(order) == "REFUNDED",
                    timeout=180
                )
            )

            check(
                "Payment REFUNDED",
                payment_status(order) == "REFUNDED"
            )

            check(
                "Exactly one successful refund",
                refund_count(order) == 1
            )

            check(
                "Wallet receives full amount",
                wallet_balance() ==
                before + Decimal(str(order["totalAmount"]))
            )

            check(
                "No key assigned",
                assigned(order) == 0
            )

        elif case == "15":
            check_success(order)

            if order_status(order) != "COMPLETED":
                raise RuntimeError(
                    "Cannot refund an incomplete purchase"
                )

            code, result = api(
                "POST",
                f"/orders/{order['id']}/refund",
                CUSTOMER
            )

            check(
                "Refund request accepted",
                code in (200, 202)
            )

            check(
                "Refund completes",
                wait_for(
                    lambda: order_status(order) == "REFUNDED"
                )
            )

            check(
                "Wallet credited exactly once",
                refund_count(order) == 1
                and wallet_balance() ==
                before + Decimal(str(order["totalAmount"]))
            )

            check(
                "Unrevealed key returned to inventory",
                t.available_count(product) == stock
            )

        elif case == "08":
            check_success(order)

            print("[ACTION] Restore browser network to Online.")

        else:
            check_success(order)

        show_state(order)

    finally:
        restore()


def summary():
    passed = sum(ok for _, ok in RESULTS)

    failed = len(RESULTS) - passed

    print("\n===== SESSION SUMMARY =====")
    print(f"PASS: {passed}")
    print(f"FAIL: {failed}")
    print(f"TOTAL ASSERTIONS: {len(RESULTS)}")


def main():
    global CUSTOMER

    print("===== OFFICIAL VNPAY SANDBOX E2E =====")
    print("Development environment only.")
    print("Some scenarios stop Docker services.")

    if input("Type TEST to continue: ") != "TEST":
        return

    preflight()

    t.MANAGER = login("catalog_manager")
    t.COORDINATOR = login("license_coordinator")
    CUSTOMER_USERNAME = (
        input("Customer [test-user1]: ").strip()
        or "test-user1"
    )

    CUSTOMER = login(CUSTOMER_USERNAME)

    while True:
        print("\n========= VNPAY SANDBOX E2E =========")

        for number, (name, _) in SCENARIOS.items():
            print(f"{number}. {name}")

        print("99. Run all scenarios")
        print("00. Exit")

        choice = input("\nSelect test case: ").strip().zfill(2)

        if choice == "00":
            break

        cases = (
            list(SCENARIOS)
            if choice == "99"
            else [choice]
        )

        for case in cases:
            if case not in SCENARIOS:
                print("Invalid selection")
                continue

            offset = len(RESULTS)

            try:
                scenario(case)

            except Exception as exc:
                print(
                    f"[ERROR] {type(exc).__name__}: {exc}"
                )

                check(f"TC{case} execution", False)

            finally:
                restore()

            current = RESULTS[offset:]
            passed = sum(ok for _, ok in current)
            failed = len(current) - passed

            print(
                f"[TC{case} RESULT] "
                f"PASS={passed} FAIL={failed}"
            )

        summary()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n[STOP] Test interrupted")
    finally:
        restore()
        summary()

    raise SystemExit(
        1 if any(not ok for _, ok in RESULTS) else 0
    )

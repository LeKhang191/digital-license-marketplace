#!/usr/bin/env python3

import os
import json
import time
import shlex
import subprocess
import importlib.util

from pathlib import Path
from urllib.parse import urlparse, parse_qs
from urllib.error import HTTPError
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
    "01": "Successful payment",
    "02": "Cancel payment",
    "03": "Abandon payment",
    "04": "Invalid card",
    "05": "Invalid OTP",
    "06": "Payment session expired",
    "07": "Network lost before payment",
    "08": "Network lost after payment",
    "09": "Payment Service offline",
    "10": "RabbitMQ offline",
    "11": "Order Service offline",
    "12": "License Vault offline",
    "13": "Out-of-stock automatic refund",
    "14": "Duplicate payment protection",
    "15": "Refund to Wallet"
}


def api(method, path, token=None, data=None):
    try:
        return 200, t.request(method, path, token, data)
    except HTTPError as exc:
        try:
            body = json.loads(exc.read())
        except Exception:
            body = {}

        return exc.code, body


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
    global CUSTOMER

    try:
        result = t.request(
            "GET",
            "/wallet/me",
            CUSTOMER
        )
    except HTTPError as exc:
        if exc.code != 401:
            raise

        print("[AUTH] Token rejected. Please login again.")

        CUSTOMER = t.login(CUSTOMER_USERNAME)

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
    deadline = time.monotonic() + timeout

    while time.monotonic() < deadline:
        if fn():
            return True

        time.sleep(2)

    return False


def wait_completed(order, timeout=180):
    return wait_for(
        lambda: order_status(order) == "COMPLETED"
        and assigned(order) == 1,
        timeout
    )


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


def scenario(case):
    print("\n" + "=" * 55)
    print(f"TC{case}: {SCENARIOS[case]}")
    print("=" * 55)

    stock = 1 if case == "13" else 3

    product = t.create_product(
        f"sandbox-{case}-{time.time_ns()}",
        stock
    )

    order = create_order(product)

    before = wallet_balance()

    try:
        # Inject faults AFTER creating the payment session.
        if case == "09":
            stop("payment")

        elif case == "10":
            stop("rabbitmq")

        elif case == "11":
            stop("order")

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
            "02": "Click Cancel on VNPay.",
            "03": "Close VNPay without paying.",
            "04": "Enter an invalid card number. Do not complete payment.",
            "05": "Enter an incorrect OTP. Do not retry with the correct OTP.",
            "06": "Wait until the VNPay payment session expires.",
            "07": "Set browser DevTools Network to Offline BEFORE confirming payment.",
            "08": "Complete payment, then set browser Offline before returning if possible.",
            "09": "Complete payment while Payment Service is offline.",
            "10": "Complete payment while RabbitMQ is offline.",
            "11": "Complete payment while Order Service is offline.",
            "12": "Complete payment while License Vault is offline.",
            "13": "Complete payment. The product is now out of stock.",
            "14": "Complete payment and refresh the result page several times.",
            "15": "Complete payment. Do NOT reveal the license key."
        }

        print("\n===== YOUR ACTION =====")
        print(instructions[case])

        pause("Finish the browser action first.")

        if case in ("02", "03", "04", "05", "07"):
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

            if case == "02":
                print(
                    "[INFO] Cancellation may remain PENDING "
                    "when no failure IPN is received."
                )

            if case == "07":
                print("[ACTION] Restore browser network to Online.")

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

        elif case == "09":
            check(
                "Payment not recorded while service is offline",
                payment_status(order) == "PENDING"
            )

            check(
                "No license while IPN cannot be processed",
                assigned(order) == 0
            )

            start("payment")

            print(
                "[INFO] Waiting for VNPay to retry its IPN."
            )

            if wait_completed(order, timeout=300):
                check_success(order)
            else:
                print(
                    "[INCONCLUSIVE] No successful callback observed. "
                    "Inspect VNPay merchant transaction history."
                )

        elif case in ("10", "11", "12"):
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

        elif case == "14":
            check_success(order)

            pause("Refresh the payment result page 5 times.")

            check(
                "Refresh does not duplicate payment",
                payment_count(order) == 1
            )

            check(
                "Refresh does not duplicate paid event",
                paid_event_count(order) == 1
            )

            check(
                "Refresh does not duplicate license",
                assigned(order) == 1
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

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")
    CUSTOMER_USERNAME = (
        input("Customer [test-user1]: ").strip()
        or "test-user1"
    )

    CUSTOMER = t.login(CUSTOMER_USERNAME)

    while True:
        print("\n========= VNPAY SANDBOX E2E =========")

        for number, name in SCENARIOS.items():
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

            try:
                scenario(case)

            except Exception as exc:
                print(
                    f"[ERROR] {type(exc).__name__}: {exc}"
                )

                check(f"TC{case} execution", False)

            finally:
                restore()

        summary()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n[STOP] Test interrupted")
    finally:
        restore()
        summary()

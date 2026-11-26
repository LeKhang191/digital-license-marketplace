#!/usr/bin/env python3

import importlib.util
import time

from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "refund_test",
    Path(__file__).with_name(
        "test-refund-integration.py"
    )
)

r = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r)

w = r.w
t = r.t
check = t.check
lit = t.literal

stopped = set()


def stop(service):
    stopped.add(service)

    t.run([
        "docker", "compose", "stop", service
    ])

    print(f"[FAULT] Stopped {service}")


def start(service):
    t.run([
        "docker", "compose", "start", service
    ])

    stopped.discard(service)

    print(f"[RECOVERY] Started {service}")


def restart(service):
    t.run([
        "docker", "compose", "restart", service
    ])

    print(f"[RECOVERY] Restarted {service}")


def state(order):
    return t.sql("order_db", f"""
        SELECT "Status"
        FROM refund_operations
        WHERE "OrderId" = {lit(order['id'])};
    """)


def retries(order):
    value = t.sql("order_db", f"""
        SELECT "RetryCount"
        FROM refund_operations
        WHERE "OrderId" = {lit(order['id'])};
    """)

    return int(value or 0)


def wait(fn, timeout=180):
    return t.wait_for(fn, timeout=timeout)


def completed(order):
    return (
        state(order) == "COMPLETED"
        and t.order_status(order["id"]) == "REFUNDED"
        and r.guard_status(order) == "FINALIZED"
        and r.refund_count(order) == 1
        and r.ledger_count(order) == 1
    )


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def purchase_count(order):
    return int(t.sql("payment_db", f"""
        SELECT count(*)
        FROM wallet_transactions w
        JOIN payments p
          ON w."ReferenceId" = p."Id"
        WHERE p."OrderId" = {lit(order['id'])}
          AND w."Type" = 'PURCHASE';
    """))


def main():
    print("===== PAYMENT & REFUND RELIABILITY =====")
    print("WARNING: Stops and restarts Docker services.")
    print("Run on isolated development environment only.")

    if input("Type TEST to continue: ") != "TEST":
        raise SystemExit("Canceled")

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")
    customer = t.login("test-user1")

    customer_id = w.subject(customer)

    w.seed_wallet(customer_id, 1500000)

    print("\n===== LICENSE SERVICE FAILURE =====")

    product_a, a = r.purchased(
        "reliability-license", customer
    )

    before = r.balance(customer)

    stop("license-vault")

    code, _ = r.refund(a, customer)

    check(
        "01. Refund request persists while License is offline",
        code in (200, 202)
        and wait(lambda: state(a) == "REQUESTED")
    )

    check(
        "02. License failure triggers retry",
        wait(lambda: retries(a) > 0)
    )

    check(
        "03. No credit before License preparation",
        r.refund_count(a) == 0
        and r.balance(customer) == before
    )

    start("license-vault")

    check(
        "04. Refund recovers after License restart",
        wait(lambda: completed(a))
    )

    check(
        "05. License recovery credits exactly once",
        r.balance(customer) == before + r.PRICE
        and t.assigned_count(a["id"]) == 0
    )

    print("\n===== PAYMENT SERVICE FAILURE =====")

    product_b, b = r.purchased(
        "reliability-payment", customer
    )

    license_b = r.owned_license(b, customer)
    before = r.balance(customer)

    stop("payment")

    code, _ = r.refund(b, customer)

    check(
        "06. Refund accepted while Payment is offline",
        code in (200, 202)
    )

    prepared = wait(
        lambda: state(b) == "PREPARED"
        and retries(b) > 0
    )

    check(
        "07. Refund persists at PREPARED",
        prepared
    )

    require(prepared, "Unable to reach PREPARED")

    check(
        "08. License remains protected",
        r.guard_status(b) == "PREPARED"
        and t.assigned_count(b["id"]) == 1
        and r.reveal(license_b, customer)[0]
            in (404, 409)
    )

    check(
        "09. No premature refund credit",
        r.refund_count(b) == 0
        and r.ledger_count(b) == 0
    )

    restart("order")

    check(
        "10. PREPARED survives Order restart",
        state(b) == "PREPARED"
        and r.guard_status(b) == "PREPARED"
    )

    start("payment")

    check(
        "11. Refund completes after Payment recovery",
        wait(lambda: completed(b))
    )

    check(
        "12. Restart does not duplicate credit",
        r.balance(customer) == before + r.PRICE
        and r.refund_count(b) == 1
        and r.ledger_count(b) == 1
    )

    print("\n===== RABBITMQ FAILURE =====")

    _, c = r.purchased(
        "reliability-broker", customer
    )

    before = r.balance(customer)

    stop("rabbitmq")

    code, _ = r.refund(c, customer)

    check(
        "13. Refund accepted during broker outage",
        code in (200, 202)
    )

    settled = wait(lambda: completed(c))

    check(
        "14. HTTP orchestration completes without broker",
        settled
    )

    require(settled, "Refund did not complete")

    check(
        "15. Broker outage does not lose refund credit",
        r.balance(customer) == before + r.PRICE
    )

    check(
        "16. Payment refund event persists in outbox",
        t.sql("payment_db", f"""
            SELECT count(*)
            FROM outbox_messages o
            JOIN payments p
              ON p."Id" = o."AggregateId"
            WHERE p."OrderId" = {lit(c['id'])}
              AND o."RoutingKey" = 'payment.refunded'
              AND o."PublishedAtUtc" IS NULL;
        """) == "1"
    )

    start("rabbitmq")

    check(
        "17. Refund outbox publishes after broker recovery",
        wait(lambda: t.sql("payment_db", f"""
            SELECT count(*)
            FROM outbox_messages o
            JOIN payments p
              ON p."Id" = o."AggregateId"
            WHERE p."OrderId" = {lit(c['id'])}
              AND o."RoutingKey" = 'payment.refunded'
              AND o."PublishedAtUtc" IS NOT NULL;
        """) == "1")
    )

    check(
        "18. Recovered refund event has no duplicate effect",
        completed(c)
        and r.balance(customer) == before + r.PRICE
    )

    print("\n===== WALLET PAYMENT RETRY =====")

    product_d = t.create_product(
        "reliability-wallet", 1
    )

    d = w.create_order(
        product_d, customer, 1
    )

    before = r.balance(customer)

    stop("rabbitmq")

    code, _ = w.pay(d, customer)

    check(
        "19. Wallet debit succeeds during broker outage",
        code == 200
        and purchase_count(d) == 1
        and r.balance(customer) == before - r.PRICE
    )

    check(
        "20. Order remains pending until payment event",
        t.order_status(d["id"]) == "PENDING"
    )

    t.disable_only_key(product_d)

    check(
        "21. Inventory changes after committed debit",
        t.available_count(product_d) == 0
    )

    code, _ = w.pay(d, customer)

    check(
        "22. Wallet retry recovers existing payment",
        code == 200
    )

    check(
        "23. Wallet retry does not debit again",
        purchase_count(d) == 1
        and r.balance(customer) == before - r.PRICE
    )

    start("rabbitmq")

    check(
        "24. Pending paid order automatically refunds",
        wait(lambda: completed(d))
    )

    check(
        "25. Wallet returns to original balance",
        r.balance(customer) == before
        and purchase_count(d) == 1
        and r.refund_count(d) == 1
        and r.ledger_count(d) == 1
    )

    print("\n===== FINAL INTEGRITY =====")

    check(
        "26. All recovery orders completed consistently",
        all(completed(x) for x in (a, b, c, d))
    )

    check(
        "27. Wallet ledger remains consistent",
        w.ledger_consistent(customer_id)
    )

    check(
        "28. No negative wallet balances",
        t.sql("payment_db", """
            SELECT count(*)
            FROM wallets
            WHERE "Balance" < 0;
        """) == "0"
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        t.FAILED += 1
        print(
            f"[ERROR] {type(exc).__name__}: {exc}"
        )
    finally:
        for service in list(stopped):
            try:
                start(service)
            except Exception as exc:
                t.FAILED += 1
                print(
                    f"[RECOVERY FAILED] {service}: {exc}"
                )

        print("\n===== RESULT =====")
        print(f"PASS: {t.PASSED}")
        print(f"FAIL: {t.FAILED}")
        print(f"TOTAL: {t.PASSED + t.FAILED}")

    raise SystemExit(
        0 if t.PASSED == 28 and t.FAILED == 0 else 1
    )

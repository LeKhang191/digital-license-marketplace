#!/usr/bin/env python3

import base64
import importlib.util
import json
import uuid

from concurrent.futures import ThreadPoolExecutor
from decimal import Decimal
from pathlib import Path
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py")
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

lit = t.literal
check = t.check
PRICE = Decimal("100000")


def subject(token):
    part = token.split(".")[1]
    part += "=" * (-len(part) % 4)

    return json.loads(
        base64.urlsafe_b64decode(part)
    )["sub"]


def api(method, path, token=None, data=None):
    try:
        return 200, t.request(
            method, path, token, data
        )
    except HTTPError as e:
        try:
            body = json.loads(e.read())
        except Exception:
            body = {}

        return e.code, body


def sql(statement):
    return t.sql("payment_db", statement)


def balance(token):
    result = t.request(
        "GET", "/wallet/me", token
    )

    return Decimal(str(result["balance"]))


def seed_wallet(customer_id, amount):
    amount = Decimal(str(amount))

    assert amount > 0

    sql(f"""
        BEGIN;

        INSERT INTO wallets (
            "Id", "CustomerId", "Balance",
            "CreatedAtUtc", "UpdatedAtUtc"
        )
        VALUES (
            {lit(uuid.uuid4())},
            {lit(customer_id)},
            0, now(), now()
        )
        ON CONFLICT ("CustomerId") DO NOTHING;

        UPDATE wallets
        SET "Balance" = "Balance" + {amount},
            "UpdatedAtUtc" = now()
        WHERE "CustomerId" = {lit(customer_id)};

        INSERT INTO wallet_transactions (
            "Id", "WalletId", "Type",
            "Amount", "BalanceAfter",
            "ReferenceId", "CreatedAtUtc"
        )
        SELECT
            {lit(uuid.uuid4())},
            "Id", 'REFUND',
            {amount}, "Balance",
            {lit(uuid.uuid4())}, now()
        FROM wallets
        WHERE "CustomerId" = {lit(customer_id)};

        COMMIT;
    """)


def create_order(product, token, quantity=1,
                 method="WALLET"):
    result = t.request(
        "POST",
        "/orders",
        token,
        {
            "productId": product,
            "quantity": quantity,
            "paymentMethod": method
        }
    )

    t.TEST_ORDERS.append(result["id"])

    return result


def pay(order, token):
    return api(
        "POST",
        f"/orders/{order['id']}/pay",
        token
    )


def payment_count(order):
    return int(sql(f"""
        SELECT count(*)
        FROM payments
        WHERE "OrderId" = {lit(order['id'])};
    """))


def ledger_count(order):
    return int(sql(f"""
        SELECT count(*)
        FROM wallet_transactions w
        JOIN payments p
          ON p."Id" = w."ReferenceId"
        WHERE p."OrderId" = {lit(order['id'])}
          AND w."Type" = 'PURCHASE';
    """))


def event_count(order, published=False):
    condition = (
        'AND o."PublishedAtUtc" IS NOT NULL'
        if published else ""
    )

    return int(sql(f"""
        SELECT count(*)
        FROM outbox_messages o
        JOIN payments p
          ON p."Id" = o."AggregateId"
        WHERE p."OrderId" = {lit(order['id'])}
          AND o."RoutingKey" = 'payment.succeeded'
          {condition};
    """))


def completed(order):
    return t.wait_for(
        lambda: t.order_status(order["id"])
            == "COMPLETED"
    )


def ledger_consistent(customer_id):
    return sql(f"""
        SELECT (
            w."Balance" = COALESCE((
                SELECT SUM(
                    CASE
                        WHEN l."Type" = 'REFUND'
                        THEN l."Amount"
                        ELSE -l."Amount"
                    END
                )
                FROM wallet_transactions l
                WHERE l."WalletId" = w."Id"
            ), 0)
        )::text
        FROM wallets w
        WHERE w."CustomerId" = {lit(customer_id)};
    """) == "true"


def main():
    print("===== WALLET PAYMENT TEST =====")

    if input("Type TEST for dev environment: ") != "TEST":
        raise SystemExit("[STOP] Test canceled")

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")

    customer = t.login("test-user1")
    other = t.login("test-user2")

    customer_id = subject(customer)
    other_id = subject(other)

    assert customer_id != other_id

    baseline = balance(customer)

    seed_wallet(customer_id, 1000000)

    initial = balance(customer)

    check(
        "01. Development wallet seeded",
        initial == baseline + 1000000
    )

    product = t.create_product("wallet", 30)

    print("\n===== BASIC PAYMENT =====")

    a = create_order(product, customer)

    check(
        "02. Create WALLET order",
        a["status"] == "PENDING"
        and a["paymentMethod"] == "WALLET"
        and not a.get("paymentUrl")
    )

    check(
        "03. Creating order does not debit wallet",
        balance(customer) == initial
        and payment_count(a) == 0
    )

    code, _ = pay(a, other)

    check(
        "04. Foreign customer cannot pay",
        code == 404
    )

    code, _ = pay(a, customer)

    check("05. Wallet payment accepted", code == 200)

    check(
        "06. Correct amount deducted",
        balance(customer) == initial - PRICE
    )

    check(
        "07. One payment and one ledger entry",
        payment_count(a) == 1
        and ledger_count(a) == 1
    )

    check(
        "08. Wallet has no fake VNPay metadata",
        sql(f"""
            SELECT count(*)
            FROM payments
            WHERE "OrderId" = {lit(a['id'])}
              AND "PaymentMethod" = 'WALLET'
              AND "Status" = 'SUCCEEDED'
              AND "VnpTransactionNo" IS NULL
              AND "VnpPayDate" IS NULL;
        """) == "1"
    )

    check(
        "09. License fulfillment completed",
        completed(a)
        and t.assigned_count(a["id"]) == 1
    )

    check(
        "10. Payment outbox published",
        t.wait_for(
            lambda: event_count(a, True) == 1
        )
    )

    print("\n===== IDEMPOTENCY =====")

    before = balance(customer)

    responses = [
        pay(a, customer)[0]
        for _ in range(5)
    ]

    check(
        "11. Repeated payment does not debit again",
        responses == [200] * 5
        and balance(customer) == before
        and ledger_count(a) == 1
    )

    b = create_order(product, customer)

    before = balance(customer)

    with ThreadPoolExecutor(max_workers=10) as pool:
        responses = list(pool.map(
            lambda _: pay(b, customer)[0],
            range(10)
        ))

    print("Concurrent responses:", responses)

    check(
        "12. Concurrent requests debit exactly once",
        200 in responses
        and balance(customer) == before - PRICE
        and payment_count(b) == 1
        and ledger_count(b) == 1
        and event_count(b) == 1
    )

    check(
        "13. Concurrent requests allocate one key",
        completed(b)
        and t.assigned_count(b["id"]) == 1
    )

    print("\n===== INSUFFICIENT BALANCE =====")

    # Use the second customer without changing its
    # existing balance. Order must exceed available funds.
    second_balance = balance(other)

    quantity = int(second_balance // PRICE) + 1

    if quantity > 10:
        raise RuntimeError(
            "test-user2 needs balance below 1,000,000 VND"
        )

    c = create_order(
        product, other, quantity
    )

    code, body = pay(c, other)

    check(
        "14. Insufficient balance rejected",
        code == 409
        and body.get("code")
            == "INSUFFICIENT_WALLET_BALANCE"
    )

    check(
        "15. Failed payment has no financial side effects",
        balance(other) == second_balance
        and payment_count(c) == 0
        and ledger_count(c) == 0
        and event_count(c) == 0
    )

    print("\n===== EXACT BALANCE AND DOUBLE SPENDING =====")

    # Make second customer's balance an exact multiple
    # of product price, without removing existing funds.
    target = (
        int(second_balance // PRICE) + 1
    ) * PRICE

    seed_wallet(
        other_id,
        target - second_balance
    )

    quantity = int(target / PRICE)

    race_product = t.create_product(
        "wallet-race", quantity * 2
    )

    x = create_order(
        race_product, other, quantity
    )

    y = create_order(
        race_product, other, quantity
    )

    with ThreadPoolExecutor(max_workers=2) as pool:
        responses = list(pool.map(
            lambda order: pay(order, other),
            [x, y]
        ))

    codes = sorted(code for code, _ in responses)

    check(
        "16. Competing orders cannot double spend",
        codes == [200, 409]
        and payment_count(x) + payment_count(y) == 1
    )

    check(
        "17. Exact balance payment ends at zero",
        balance(other) == 0
        and ledger_count(x) + ledger_count(y) == 1
    )

    winner = x if payment_count(x) == 1 else y

    check(
        "18. Winning order fulfilled",
        completed(winner)
        and t.assigned_count(winner["id"]) == quantity
    )

    print("\n===== DATA INTEGRITY =====")

    check(
        "19. Wallet ledger matches balance",
        ledger_consistent(customer_id)
        and ledger_consistent(other_id)
    )

    code, _ = api(
        "POST",
        "/orders",
        customer,
        {
            "productId": product,
            "quantity": 1,
            "paymentMethod": "INVALID"
        }
    )

    check(
        "20. Invalid payment method blocked",
        code == 400
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        t.FAILED += 1
        print(f"[ERROR] {type(exc).__name__}: {exc}")
    finally:
        print("\n===== RESULT =====")
        print(f"RUN ID: {t.RUN_ID}")
        print(f"PASS: {t.PASSED}")
        print(f"FAIL: {t.FAILED}")
        print(f"TOTAL: {t.PASSED + t.FAILED}")

        print("\nTEST ORDER IDS:")

        for order_id in t.TEST_ORDERS:
            print(order_id)

    raise SystemExit(
        0 if t.PASSED == 20 and t.FAILED == 0 else 1
    )

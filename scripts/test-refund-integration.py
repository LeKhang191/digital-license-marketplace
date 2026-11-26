#!/usr/bin/env python3

import importlib.util
import uuid

from pathlib import Path
from concurrent.futures import ThreadPoolExecutor

spec = importlib.util.spec_from_file_location(
    "wallet_test",
    Path(__file__).with_name("test-wallet-payment.py")
)

w = importlib.util.module_from_spec(spec)
spec.loader.exec_module(w)

t = w.t
api = w.api
lit = t.literal
check = t.check
balance = w.balance
PRICE = w.PRICE


def refund(order, token):
    return api(
        "POST",
        f"/orders/{order['id']}/refund",
        token
    )


def refund_status(order, token):
    code, result = api(
        "GET",
        f"/orders/{order['id']}/refund",
        token
    )

    return result if code == 200 else {}


def wait_status(order, token, expected):
    return t.wait_for(
        lambda: refund_status(order, token)
            .get("status") == expected,
        timeout=120
    )


def refund_count(order):
    return int(t.sql("payment_db", f"""
        SELECT count(*)
        FROM refunds r
        JOIN payments p
          ON p."Id" = r."PaymentId"
        WHERE p."OrderId" = {lit(order['id'])}
          AND r."Status" = 'SUCCEEDED'
          AND r."Amount" = p."Amount";
    """))


def ledger_count(order):
    return int(t.sql("payment_db", f"""
        SELECT count(*)
        FROM wallet_transactions w
        JOIN refunds r
          ON r."Id" = w."ReferenceId"
        JOIN payments p
          ON p."Id" = r."PaymentId"
        WHERE p."OrderId" = {lit(order['id'])}
          AND w."Type" = 'REFUND'
          AND w."Amount" = p."Amount";
    """))


def guard_status(order):
    return t.sql("license_db", f"""
        SELECT "Status"
        FROM license_refund_guards
        WHERE "OrderId" = {lit(order['id'])};
    """)


def payment_status(order):
    return t.sql("payment_db", f"""
        SELECT "Status"
        FROM payments
        WHERE "OrderId" = {lit(order['id'])};
    """)


def owned_license(order, token):
    result = t.request(
        "GET",
        f"/licenses/me?orderId={order['id']}",
        token
    )

    if isinstance(result, dict):
        result = result["items"]

    if len(result) != 1:
        raise RuntimeError(
            "Expected exactly one owned license"
        )

    return result[0]["id"]


def reveal(license_id, token):
    return api(
        "POST",
        f"/licenses/me/{license_id}/reveal",
        token
    )


def ticket(order, license_id, customer):
    code, result = api(
        "POST",
        "/support/tickets",
        customer,
        {
            "orderId": order["id"],
            "licenseId": license_id,
            "category": "INVALID_KEY",
            "subject": "Development defective key test",
            "description": "Synthetic integration test only"
        }
    )

    assert code == 200, result

    return result["id"]


def claim(ticket_id, coordinator):
    return api(
        "PATCH",
        f"/support/tickets/{ticket_id}/status",
        coordinator,
        {"status": "IN_PROGRESS"}
    )


def approve(ticket_id, token):
    return api(
        "POST",
        f"/support/tickets/{ticket_id}/refund",
        token
    )


def purchased(name, token, quantity=1):
    product = t.create_product(
        name, quantity + 2
    )

    order = w.create_order(
        product, token, quantity
    )

    code, _ = w.pay(order, token)

    if code != 200 or not w.completed(order):
        raise RuntimeError(
            f"Unable to purchase test order {order['id']}"
        )

    return product, order


def main():
    print("===== REFUND INTEGRATION TEST =====")
    print("DEV ONLY: creates synthetic wallet credits and keys.")

    if input("Type TEST for development environment: ") != "TEST":
        raise SystemExit("Canceled")

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")

    auditor = t.login("transaction_auditor")
    customer = t.login("test-user1")
    other = t.login("test-user2")

    customer_id = w.subject(customer)

    w.seed_wallet(customer_id, 2000000)

    product, a = purchased("refund-unrevealed", customer)

    license_a = owned_license(a, customer)
    before = balance(customer)
    stock_before = t.available_count(product)

    print("\n===== AUTHORIZATION =====")

    check(
        "01. Anonymous refund blocked",
        refund(a, None)[0] == 401
    )

    check(
        "02. Foreign customer refund blocked",
        refund(a, other)[0] == 404
    )

    check(
        "03. Catalog manager refund blocked",
        refund(a, t.MANAGER)[0] == 403
    )

    check(
        "04. Auditor cannot use customer refund API",
        refund(a, auditor)[0] == 403
    )

    check(
        "05. Customer cannot access internal refund API",
        api(
            "POST",
            "/internal/refunds/wallet",
            customer,
            {}
        )[0] in (401, 403, 404)
    )

    print("\n===== CUSTOMER REFUND =====")

    code, result = refund(a, customer)

    request_id = result.get("requestId")

    check(
        "06. Customer refund accepted",
        code == 200 and bool(request_id)
    )

    check(
        "07. Refund operation completes",
        wait_status(a, customer, "COMPLETED")
    )

    check(
        "08. Order becomes REFUNDED",
        t.order_status(a["id"]) == "REFUNDED"
    )

    check(
        "09. Payment becomes REFUNDED",
        payment_status(a) == "REFUNDED"
    )

    check(
        "10. Full amount credited",
        balance(customer) == before + PRICE
    )

    check(
        "11. Exactly one refund and ledger entry",
        refund_count(a) == ledger_count(a) == 1
    )

    check(
        "12. Unrevealed key returned to stock",
        t.available_count(product) == stock_before + 1
        and t.assigned_count(a["id"]) == 0
    )

    check(
        "13. License guard finalized",
        guard_status(a) == "FINALIZED"
    )

    check(
        "14. Refunded license cannot be revealed",
        reveal(license_a, customer)[0] in (404, 409)
    )

    check(
        "15. Foreign customer cannot view refund",
        api(
            "GET",
            f"/orders/{a['id']}/refund",
            other
        )[0] == 404
    )

    print("\n===== IDEMPOTENCY =====")

    before = balance(customer)

    responses = [
        refund(a, customer)
        for _ in range(5)
    ]

    check(
        "16. Repeated refund returns same request",
        all(
            code == 200
            and body.get("requestId") == request_id
            for code, body in responses
        )
    )

    check(
        "17. Repeated refund does not credit again",
        balance(customer) == before
        and refund_count(a) == ledger_count(a) == 1
    )

    _, b = purchased("refund-concurrent", customer)

    before = balance(customer)

    with ThreadPoolExecutor(max_workers=10) as pool:
        responses = list(pool.map(
            lambda _: refund(b, customer),
            range(10)
        ))

    request_ids = {
        body.get("requestId")
        for code, body in responses
        if code == 200
    }

    check(
        "18. Concurrent requests share one operation",
        all(code == 200 for code, _ in responses)
        and len(request_ids) == 1
        and None not in request_ids
    )

    check(
        "19. Concurrent refund credits exactly once",
        wait_status(b, customer, "COMPLETED")
        and balance(customer) == before + PRICE
        and refund_count(b) == ledger_count(b) == 1
    )

    print("\n===== REVEAL PROTECTION =====")

    product_c, c = purchased("refund-revealed", customer)

    license_c = owned_license(c, customer)

    check(
        "20. Customer reveals owned license",
        reveal(license_c, customer)[0] == 200
    )

    before = balance(customer)

    refund(c, customer)

    check(
        "21. Revealed key refund rejected",
        wait_status(c, customer, "REJECTED")
        and refund_status(c, customer)
            .get("rejectionCode")
            == "LICENSE_ALREADY_REVEALED"
    )

    rejected_id = refund_status(
        c, customer
    ).get("requestId")

    check(
        "22. Rejection has no financial effects",
        refund_count(c) == 0
        and ledger_count(c) == 0
        and balance(customer) == before
        and t.order_status(c["id"]) == "COMPLETED"
        and guard_status(c) == ""
    )

    print("\n===== VERIFIED DEFECT REFUND =====")

    ticket_c = ticket(c, license_c, customer)

    check(
        "23. Customer cannot approve defect refund",
        approve(ticket_c, customer)[0] == 403
    )

    check(
        "24. Auditor cannot approve license defect",
        approve(ticket_c, auditor)[0] == 403
    )

    check(
        "25. Unassigned coordinator cannot approve",
        approve(ticket_c, t.COORDINATOR)[0] == 404
    )

    check(
        "26. Coordinator claims ticket",
        claim(ticket_c, t.COORDINATOR)[0] == 200
    )

    before = balance(customer)

    code, result = approve(
        ticket_c, t.COORDINATOR
    )

    defect_request = result.get("requestId")

    check(
        "27. Rejected customer refund can be escalated",
        code == 200
        and bool(defect_request)
        and defect_request != rejected_id
        and result.get("mode") == "VERIFIED_DEFECT"
    )

    check(
        "28. Verified defect refund completes",
        wait_status(c, customer, "COMPLETED")
        and t.order_status(c["id"]) == "REFUNDED"
    )

    check(
        "29. Defect refund credits exactly once",
        balance(customer) == before + PRICE
        and refund_count(c) == ledger_count(c) == 1
    )

    check(
        "30. Disclosed defective license disabled",
        t.sql("license_db", f"""
            SELECT "Status"
            FROM license_keys
            WHERE "Id" = {lit(license_c)};
        """) == "DISABLED"
        and guard_status(c) == "FINALIZED"
    )

    check(
        "31. Repeated staff approval is idempotent",
        approve(ticket_c, t.COORDINATOR)[1]
            .get("requestId") == defect_request
        and refund_count(c) == 1
    )

    check(
        "32. Staff can view refund progress",
        api(
            "GET",
            f"/support/tickets/{ticket_c}/refund",
            t.COORDINATOR
        )[1].get("status") == "COMPLETED"
    )

    check(
        "33. Refund prevents later replacement",
        api(
            "POST",
            f"/licenses/replacements/{ticket_c}",
            t.COORDINATOR
        )[0] == 409
    )

    print("\n===== REPLACEMENT PROTECTION =====")

    _, d = purchased("refund-replaced", customer)

    license_d = owned_license(d, customer)

    reveal(license_d, customer)

    ticket_d = ticket(d, license_d, customer)
    claim(ticket_d, t.COORDINATOR)

    code, _ = api(
        "POST",
        f"/licenses/replacements/{ticket_d}",
        t.COORDINATOR
    )

    check(
        "34. Replacement completed",
        code == 200
    )

    before = balance(customer)

    refund(d, customer)

    check(
        "35. Replaced order cannot be refunded",
        wait_status(d, customer, "REJECTED")
        and refund_status(d, customer)
            .get("rejectionCode")
            == "ORDER_ALREADY_COMPENSATED"
        and balance(customer) == before
        and refund_count(d) == 0
    )

    print("\n===== REVEAL VS REFUND RACE =====")

    race_ok = True

    for i in range(3):
        _, order = purchased(
            f"refund-race-{i}", customer
        )

        license_id = owned_license(order, customer)

        before = balance(customer)

        with ThreadPoolExecutor(max_workers=2) as pool:
            reveal_future = pool.submit(
                reveal, license_id, customer
            )

            refund_future = pool.submit(
                refund, order, customer
            )

            reveal_code, _ = reveal_future.result()
            refund_code, _ = refund_future.result()

        settled = t.wait_for(
            lambda: refund_status(order, customer)
                .get("status") in (
                    "COMPLETED", "REJECTED"
                ),
            timeout=120
        )

        state = refund_status(
            order, customer
        ).get("status")

        if state == "COMPLETED":
            valid = (
                reveal_code in (404, 409)
                and balance(customer) == before + PRICE
                and refund_count(order) == 1
            )
        elif state == "REJECTED":
            valid = (
                reveal_code == 200
                and balance(customer) == before
                and refund_count(order) == 0
            )
        else:
            valid = False

        race_ok = (
            race_ok
            and settled
            and refund_code == 200
            and valid
        )

        print(
            f"Race {i+1}: reveal={reveal_code}, "
            f"refund={state}"
        )

    check(
        "36. Reveal and refund never both succeed",
        race_ok
    )

    print("\n===== DATA INTEGRITY =====")

    check(
        "37. No negative wallet balance",
        t.sql("payment_db", """
            SELECT count(*)
            FROM wallets
            WHERE "Balance" < 0;
        """) == "0"
    )

    check(
        "38. No duplicated successful refunds",
        t.sql("payment_db", """
            SELECT count(*) FROM (
                SELECT "PaymentId"
                FROM refunds
                WHERE "Status" = 'SUCCEEDED'
                GROUP BY "PaymentId"
                HAVING count(*) > 1
            ) duplicates;
        """) == "0"
    )

    check(
        "39. Wallet balance matches ledger",
        w.ledger_consistent(customer_id)
    )

    check(
        "40. Completed refund generates order event",
        t.sql("order_db", f"""
            SELECT count(*)
            FROM outbox_messages
            WHERE "AggregateId" = {lit(a['id'])}
              AND "RoutingKey" = 'order.refunded';
        """) == "1"
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
        print("\n===== RESULT =====")
        print(f"RUN ID: {t.RUN_ID}")
        print(f"PASS: {t.PASSED}")
        print(f"FAIL: {t.FAILED}")
        print(f"TOTAL: {t.PASSED + t.FAILED}")

        print("\nTEST ORDER IDS:")

        for order_id in t.TEST_ORDERS:
            print(order_id)

    raise SystemExit(
        0 if t.PASSED == 40 and t.FAILED == 0 else 1
    )

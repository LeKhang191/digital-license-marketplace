#!/usr/bin/env python3

import os
import json
import hmac
import hashlib
import uuid
import base64
import importlib.util

from datetime import datetime, timezone, timedelta
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.parse import urlencode, urlparse, parse_qs
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py")
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

TMN = os.environ.get("VNPAY_TMN_CODE", "")
SECRET = os.environ.get("VNPAY_HASH_SECRET", "")

if TMN != "TEST0001" or not SECRET:
    raise SystemExit(
        "[STOP] Configure isolated VNPay test credentials first"
    )

runtime = t.run([
    "docker", "compose", "exec", "-T", "payment",
    "sh", "-c",
    'test "$VnPay__TmnCode" = TEST0001 && '
    'printf "%s" "$VnPay__HashSecret"'
])

if not hmac.compare_digest(runtime, SECRET):
    raise SystemExit("[STOP] Runtime configuration mismatch")

print("\n===== LOGIN =====")

t.MANAGER = t.login(input("Catalog Manager username: ").strip())
t.COORDINATOR = t.login(input("License Coordinator username: ").strip())

CUSTOMER = t.login(input("Customer username: ").strip())
OTHER = t.login(input("Second Customer username: ").strip())

def subject(token):
    part = token.split(".")[1]
    part += "=" * (-len(part) % 4)
    return json.loads(base64.urlsafe_b64decode(part))["sub"]

if subject(CUSTOMER) == subject(OTHER):
    raise SystemExit("[STOP] Use two different customers")

def api(method, path, token=None, data=None):
    try:
        return 200, t.request(method, path, token, data)
    except HTTPError as e:
        try:
            body = json.loads(e.read())
        except Exception:
            body = {}
        return e.code, body

def sql(statement):
    return t.sql("order_db", statement)

def lit(value):
    return t.literal(value)

def payment_sql(statement):
    return t.sql("payment_db", statement)

def payment_status(order):
    return payment_sql(f"""
        SELECT "Status"
        FROM payments
        WHERE "OrderId" = {lit(order['id'])};
    """).strip()

def payment_count(order):
    return int(payment_sql(f"""
        SELECT count(*)
        FROM payments
        WHERE "OrderId" = {lit(order['id'])};
    """))

def payment_ref_matches(order):
    return int(payment_sql(f"""
        SELECT count(*)
        FROM payments
        WHERE "OrderId" = {lit(order['id'])}
          AND "PaymentRef" = {lit(order['ref'])};
    """)) == 1

def payment_event_count(order, route, published=False):
    condition = (
        'AND o."PublishedAtUtc" IS NOT NULL'
        if published else ''
    )

    return int(payment_sql(f"""
        SELECT count(*)
        FROM outbox_messages o
        JOIN payments p
          ON p."Id" = o."AggregateId"
        WHERE p."OrderId" = {lit(order['id'])}
          AND o."RoutingKey" = {lit(route)}
          {condition};
    """))

def payment_succeeded(order):
    return payment_status(order) == "SUCCEEDED"

def order_paid(order):
    return status(order) in ("PAID", "COMPLETED")

def order_event_published(order):
    return int(sql(f"""
        SELECT count(*)
        FROM outbox_messages
        WHERE "AggregateId" = {lit(order['id'])}
          AND "RoutingKey" = 'order.paid'
          AND "PublishedAtUtc" IS NOT NULL;
    """)) == 1

def assigned_count(order):
    return int(t.sql("license_db", f"""
        SELECT count(*)
        FROM license_keys
        WHERE "OrderId" = {lit(order['id'])}
          AND "Status" = 'ASSIGNED';
    """))

def create_order(product):
    result = t.request(
        "POST",
        "/orders",
        CUSTOMER,
        {"productId": product, "quantity": 1}
    )

    params = parse_qs(
        urlparse(result["paymentUrl"]).query
    )

    return {
        "id": result["id"],
        "ref": params["vnp_TxnRef"][0],
        "amount": params["vnp_Amount"][0],
        "created": params["vnp_CreateDate"][0],
        "expires": params["vnp_ExpireDate"][0],
        "tx": str(uuid.uuid4().int % 10**12 + 1),
    }

def details(order):
    return t.request(
        "GET",
        f"/orders/{order['id']}",
        CUSTOMER
    )

def status(order):
    return details(order)["status"]

def outbox_count(order):
    return int(sql(f"""
        SELECT count(*)
        FROM outbox_messages
        WHERE "AggregateId" = {lit(order['id'])}
          AND "RoutingKey" = 'order.paid';
    """))

def params(order, **overrides):
    p = {
        "vnp_Amount": order["amount"],
        "vnp_BankCode": "NCB",
        "vnp_CardType": "ATM",
        "vnp_OrderInfo": "DevelopmentTest",
        "vnp_PayDate": (
            datetime.now(timezone.utc)
            + timedelta(hours=7)
        ).strftime("%Y%m%d%H%M%S"),
        "vnp_ResponseCode": "00",
        "vnp_TmnCode": TMN,
        "vnp_TransactionNo": order["tx"],
        "vnp_TransactionStatus": "00",
        "vnp_TxnRef": order["ref"],
    }

    p.update(overrides)
    return p

def ipn(order, mode="valid", **overrides):
    p = params(order, **overrides)
    query = urlencode(sorted(
        (k, v) for k, v in p.items() if v
    ))

    signature = hmac.new(
        SECRET.encode(),
        query.encode(),
        hashlib.sha512
    ).hexdigest()

    if mode == "invalid":
        signature = "0" * 128

    if mode != "missing":
        query += "&vnp_SecureHash=" + signature

    result = t.request(
        "GET",
        "/orders/vnpay-ipn?" + query
    )

    return result.get("RspCode")

def fail_ipn(order):
    return ipn(
        order,
        vnp_ResponseCode="24",
        vnp_TransactionStatus="02"
    )

check = t.check

print("\n===== PAYMENT LIFECYCLE =====")

product = t.create_product("vnpay-main", 20)

a = create_order(product)

check("01. Create pending order",
      status(a) == "PENDING")

pay = t.request(
    "POST",
    f"/orders/{a['id']}/pay",
    CUSTOMER
)

p = parse_qs(urlparse(pay["paymentUrl"]).query)

check("02. PaymentRef remains stable",
      p["vnp_TxnRef"][0] == a["ref"])

check("03. Payment dates remain stable",
      p["vnp_CreateDate"][0] == a["created"]
      and p["vnp_ExpireDate"][0] == a["expires"])

expired = create_order(product)

payment_sql(f"""
    UPDATE payments
    SET "ExpiresAtUtc" = now() - interval '1 day'
    WHERE "OrderId" = {lit(expired['id'])};
""")

code, body = api(
    "POST",
    f"/orders/{expired['id']}/pay",
    CUSTOMER
)

check("04. Expired payment rejected",
      code == 409
      and body.get("code") == "PAYMENT_EXPIRED")

code, _ = api(
    "POST",
    f"/orders/{a['id']}/pay",
    OTHER
)

check("05. Other customer cannot pay order",
      code == 404)

print("\n===== IPN VALIDATION =====")

check("06. Invalid signature rejected",
      ipn(a, mode="invalid") == "97")

check("07. Missing signature rejected",
      ipn(a, mode="missing") == "97")

check("08. Invalid merchant rejected",
      ipn(a, vnp_TmnCode="WRONG001") == "99")

check("09. Invalid amount rejected",
      ipn(a, vnp_Amount="1") == "04")

check("10. Invalid PaymentRef rejected",
      ipn(a, vnp_TxnRef=a["ref"] + "9") == "01")

check("11. Unknown order rejected",
      ipn(a, vnp_TxnRef=uuid.uuid4().hex) == "01")

check("12. Invalid transaction number rejected",
      ipn(a, vnp_TransactionNo="INVALID") == "99")

check("13. Invalid payment date rejected",
      ipn(a, vnp_PayDate="20261399129999") == "99")

check("14. Invalid callbacks do not mutate order or payment",
      status(a) == "PENDING"
      and payment_status(a) == "PENDING"
      and payment_event_count(a, "payment.succeeded") == 0
      and payment_event_count(a, "payment.failed") == 0
      and outbox_count(a) == 0)

print("\n===== PAYMENT BUSINESS LOGIC =====")

check("15. Valid payment accepted",
      ipn(a) == "00")

check("16. Duplicate payment rejected",
      ipn(a) == "02")

check("17. Exactly one successful payment and order event",
      payment_event_count(a, "payment.succeeded") == 1
      and t.wait_for(lambda: outbox_count(a) == 1))

check("18. Failed IPN cannot reverse payment",
      fail_ipn(a) == "02"
      and payment_succeeded(a)
      and t.wait_for(lambda: order_paid(a)))

check("19. Conflicting transaction detected",
      ipn(a, vnp_TransactionNo="9999999999999") == "99")

check("20. Automatic license fulfillment",
      t.wait_for(lambda: status(a) == "COMPLETED"))

check("21. FulfillmentStatus ASSIGNED",
      details(a)["fulfillmentStatus"] == "ASSIGNED")

b = create_order(product)

check("22. Failed payment cancels order",
      fail_ipn(b) == "00"
      and payment_status(b) == "FAILED"
      and t.wait_for(lambda: status(b) == "CANCELED")
      and payment_event_count(b, "payment.failed") == 1
      and outbox_count(b) == 0)

check("23. Late successful IPN recovered",
      ipn(b) == "00"
      and payment_succeeded(b)
      and t.wait_for(lambda: order_paid(b))
      and t.wait_for(lambda: outbox_count(b) == 1))

print("\n===== OUT OF STOCK =====")

limited = t.create_product("vnpay-limited", 1)

first = create_order(limited)
second = create_order(limited)

if ipn(first) != "00":
    raise RuntimeError("Cannot prepare stock test")

if not t.wait_for(
    lambda: status(first) == "COMPLETED"
):
    raise RuntimeError("First allocation did not complete")

from decimal import Decimal

wallet_before = Decimal(str(
    t.request("GET", "/wallet/me", CUSTOMER)["balance"]
))

if ipn(second) != "00":
    raise RuntimeError("Second payment was not accepted")

check("24. Out-of-stock order automatically refunded",
      t.wait_for(
          lambda: status(second) == "REFUNDED",
          timeout=120
      )
      and details(second)["fulfillmentStatus"]
          == "OUT_OF_STOCK")

print("\n===== CONCURRENCY =====")

c = create_order(product)

with ThreadPoolExecutor(max_workers=8) as pool:
    responses = list(
        pool.map(lambda _: ipn(c), range(8))
    )

check("25. Concurrent IPNs accepted once",
      responses.count("00") == 1
      and responses.count("02") == 7)

check("26. Concurrent IPNs create one event per service",
      payment_event_count(c, "payment.succeeded") == 1
      and t.wait_for(lambda: outbox_count(c) == 1))

d = create_order(product)

with ThreadPoolExecutor(max_workers=2) as pool:
    f1 = pool.submit(ipn, d)
    f2 = pool.submit(fail_ipn, d)
    responses = [f1.result(), f2.result()]

check("27. Concurrent success and failure",
      "00" in responses
      and payment_succeeded(d)
      and t.wait_for(lambda: order_paid(d))
      and t.wait_for(lambda: outbox_count(d) == 1)
      and payment_event_count(d, "payment.succeeded") == 1)

check("28. Concurrent order completed",
      t.wait_for(lambda: status(c) == "COMPLETED"))

assigned = int(t.sql("license_db", f"""
    SELECT count(*)
    FROM license_keys
    WHERE "OrderId" = {lit(c['id'])}
      AND "Status" = 'ASSIGNED';
"""))

check("29. No duplicate license allocation",
      assigned == 1)


print("\n===== PAYMENT MICROSERVICE INTEGRATION =====")

check("30. One payment per order",
      payment_count(a) == 1)

check("31. PaymentRef matches Payment database",
      payment_ref_matches(a))

check("32. PaymentRef matches Order database",
      int(sql(f"""
          SELECT count(*)
          FROM orders
          WHERE "Id" = {lit(a['id'])}
            AND "PaymentRef" = {lit(a['ref'])};
      """)) == 1)

check("33. Payment status SUCCEEDED",
      payment_succeeded(a))

check("34. Payment outbox event published",
      t.wait_for(
          lambda: payment_event_count(
              a, "payment.succeeded", published=True
          ) == 1
      ))

check("35. Order outbox event published",
      t.wait_for(lambda: order_event_published(a)))

check("36. Failed event published",
      t.wait_for(
          lambda: payment_event_count(
              b, "payment.failed", published=True
          ) == 1
      ))

print("\n===== CROSS-PAYMENT TRANSACTION VALIDATION =====")

e = create_order(product)

check("37. Duplicate provider transaction rejected",
      ipn(e, vnp_TransactionNo=a["tx"]) == "99")

check("38. Duplicate transaction leaves payment pending",
      payment_status(e) == "PENDING"
      and status(e) == "PENDING"
      and payment_event_count(e, "payment.succeeded") == 0)

check("39. Different transaction accepted",
      ipn(e) == "00"
      and payment_succeeded(e))

check("40. New payment completed end-to-end",
      t.wait_for(lambda: status(e) == "COMPLETED"))

check("41. Exactly one license allocated",
      assigned_count(e) == 1)

print("\n===== PAYMENT EVENT IDEMPOTENCY =====")

check("42. Duplicate IPNs remain idempotent",
      all(ipn(e) == "02" for _ in range(5)))

check("43. Exactly one successful payment event",
      payment_event_count(e, "payment.succeeded") == 1)

check("44. Exactly one order paid event",
      outbox_count(e) == 1)

check("45. No duplicate license after repeated IPNs",
      assigned_count(e) == 1)

check("46. Concurrent success/failure eventually completed",
      t.wait_for(lambda: status(d) == "COMPLETED")
      and assigned_count(d) == 1)

check("47. Expired session remains unpaid",
      payment_status(expired) == "PENDING"
      and payment_event_count(
          expired, "payment.succeeded"
      ) == 0)


print("\n===== AUTOMATIC REFUND VALIDATION =====")

check("48. Payment marked REFUNDED",
      payment_status(second) == "REFUNDED")

check("49. Exactly one successful refund",
      payment_sql(f"""
          SELECT count(*)
          FROM refunds r
          JOIN payments p
            ON p."Id" = r."PaymentId"
          WHERE p."OrderId" = {lit(second['id'])}
            AND r."Status" = 'SUCCEEDED'
            AND r."Amount" = p."Amount";
      """) == "1")

wallet_after = Decimal(str(
    t.request("GET", "/wallet/me", CUSTOMER)["balance"]
))

expected_credit = Decimal(second["amount"]) / 100

check("50. Full payment amount credited to wallet",
      wallet_after == wallet_before + expected_credit)

check("51. Exactly one refund ledger entry",
      payment_sql(f"""
          SELECT count(*)
          FROM wallet_transactions w
          JOIN refunds r
            ON r."Id" = w."ReferenceId"
          JOIN payments p
            ON p."Id" = r."PaymentId"
          WHERE p."OrderId" = {lit(second['id'])}
            AND w."Type" = 'REFUND'
            AND w."Amount" = p."Amount";
      """) == "1")

check("52. Orchestration completed without assigning keys",
      sql(f"""
          SELECT count(*)
          FROM refund_operations
          WHERE "OrderId" = {lit(second['id'])}
            AND "Status" = 'COMPLETED'
            AND "Mode" = 'OUT_OF_STOCK';
      """) == "1"
      and assigned_count(second) == 0
      and t.sql("license_db", f"""
          SELECT count(*)
          FROM license_refund_guards
          WHERE "OrderId" = {lit(second['id'])}
            AND "Status" = 'FINALIZED';
      """) == "1"
      and t.wait_for(
          lambda: payment_event_count(
              second, "payment.refunded", published=True
          ) == 1
      ))

print("\n===== RESULT =====")
print(f"PASS: {t.PASSED}")
print(f"FAIL: {t.FAILED}")
print(f"TOTAL: {t.PASSED + t.FAILED}")

raise SystemExit(1 if t.FAILED else 0)

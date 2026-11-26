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
    "docker", "compose", "exec", "-T", "order",
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

sql(f"""
    UPDATE orders
    SET "CreatedAtUtc" = now() - interval '1 day'
    WHERE "Id" = {lit(expired['id'])};
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

check("14. Invalid callbacks do not mutate order",
      status(a) == "PENDING"
      and outbox_count(a) == 0)

print("\n===== PAYMENT BUSINESS LOGIC =====")

check("15. Valid payment accepted",
      ipn(a) == "00")

check("16. Duplicate payment rejected",
      ipn(a) == "02")

check("17. Exactly one outbox event",
      outbox_count(a) == 1)

check("18. Failed IPN cannot reverse payment",
      fail_ipn(a) == "02"
      and status(a) in ("PAID", "COMPLETED"))

check("19. Conflicting transaction detected",
      ipn(a, vnp_TransactionNo="9999999999999") == "99")

check("20. Automatic license fulfillment",
      t.wait_for(lambda: status(a) == "COMPLETED"))

check("21. FulfillmentStatus ASSIGNED",
      details(a)["fulfillmentStatus"] == "ASSIGNED")

b = create_order(product)

check("22. Failed payment cancels order",
      fail_ipn(b) == "00"
      and status(b) == "CANCELED"
      and outbox_count(b) == 0)

check("23. Late successful IPN recovered",
      ipn(b) == "00"
      and status(b) in ("PAID", "COMPLETED")
      and outbox_count(b) == 1)

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

if ipn(second) != "00":
    raise RuntimeError("Second payment was not accepted")

check("24. Paid order without stock is reported",
      t.wait_for(
          lambda: details(second)["fulfillmentStatus"]
          == "OUT_OF_STOCK"
      )
      and status(second) == "PAID")

print("\n===== CONCURRENCY =====")

c = create_order(product)

with ThreadPoolExecutor(max_workers=8) as pool:
    responses = list(
        pool.map(lambda _: ipn(c), range(8))
    )

check("25. Concurrent IPNs accepted once",
      responses.count("00") == 1
      and responses.count("02") == 7)

check("26. Concurrent IPNs create one event",
      outbox_count(c) == 1)

d = create_order(product)

with ThreadPoolExecutor(max_workers=2) as pool:
    f1 = pool.submit(ipn, d)
    f2 = pool.submit(fail_ipn, d)
    responses = [f1.result(), f2.result()]

check("27. Concurrent success and failure",
      "00" in responses
      and status(d) in ("PAID", "COMPLETED")
      and outbox_count(d) == 1)

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

print("\n===== RESULT =====")
print(f"PASS: {t.PASSED}")
print(f"FAIL: {t.FAILED}")
print(f"TOTAL: {t.PASSED + t.FAILED}")

raise SystemExit(1 if t.FAILED else 0)

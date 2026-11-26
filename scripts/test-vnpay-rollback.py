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

if input("Type TEST for development environment: ") != "TEST":
    raise SystemExit("Canceled")

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
    return t.sql("payment_db", statement)

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
        WHERE "AggregateId" = (
            SELECT "Id" FROM payments
            WHERE "OrderId" = {lit(order['id'])}
        )
          AND "RoutingKey" = 'payment.succeeded';
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


print("\n===== VNPAY TRANSACTION ROLLBACK =====")

product = t.create_product("payment-rollback", 3)
order = create_order(product)

oid = lit(order["id"])

suffix = uuid.uuid4().hex[:12]
trigger = f"test_payment_rollback_{suffix}"
function = f"test_payment_failure_{suffix}"

def assigned_count():
    return int(t.sql("license_db", f"""
        SELECT count(*)
        FROM license_keys
        WHERE "OrderId" = {oid}
          AND "Status" = 'ASSIGNED';
    """))

def payment_empty():
    return sql(f"""
        SELECT count(*)
        FROM payments
        WHERE "OrderId" = {oid}
          AND "Status" = 'PENDING'
          AND "PaidAtUtc" IS NULL
          AND "VnpTransactionNo" IS NULL;
    """) == "1"

check("01. Initial order pending",
      status(order) == "PENDING")

try:
    sql(f"""
        CREATE FUNCTION {function}()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            IF NEW."AggregateId" = (
                SELECT "Id" FROM payments
                WHERE "OrderId" = {oid}
            )
               AND NEW."RoutingKey" = 'payment.succeeded'
            THEN
                RAISE EXCEPTION 'TEST_PAYMENT_OUTBOX_FAILURE';
            END IF;

            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER {trigger}
        BEFORE INSERT ON outbox_messages
        FOR EACH ROW
        EXECUTE FUNCTION {function}();
    """)

    check("02. Failed transaction returns 99",
          ipn(order) == "99")

    check("03. Order status rolled back",
          status(order) == "PENDING")

    check("04. Payment metadata rolled back",
          payment_empty())

    check("05. No outbox event after rollback",
          outbox_count(order) == 0)

    check("06. No license assigned after rollback",
          assigned_count() == 0)

finally:
    sql(f"""
        DROP TRIGGER IF EXISTS {trigger}
        ON outbox_messages;

        DROP FUNCTION IF EXISTS {function}();
    """)

    print("[CLEANUP] Failure injection removed")

print("\n===== RETRY AFTER ROLLBACK =====")

check("07. Retried payment succeeds",
      ipn(order) == "00")

check("08. Exactly one outbox event",
      outbox_count(order) == 1)

check("09. Order completed after retry",
      t.wait_for(lambda: status(order) == "COMPLETED"))

check("10. Exactly one license assigned",
      assigned_count() == 1)

check("11. Fulfillment status assigned",
      details(order)["fulfillmentStatus"] == "ASSIGNED")

check("12. Duplicate retry rejected",
      ipn(order) == "02"
      and assigned_count() == 1
      and outbox_count(order) == 1)

print("\n===== RESULT =====")
print(f"PASS: {t.PASSED}")
print(f"FAIL: {t.FAILED}")
print(f"TOTAL: {t.PASSED + t.FAILED}")

raise SystemExit(1 if t.FAILED else 0)

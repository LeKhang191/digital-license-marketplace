#!/usr/bin/env python3

import importlib.util
import json
import sys

from pathlib import Path
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py")
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

passed = 0
failed = 0


def check(name, condition):
    global passed, failed

    if condition:
        passed += 1
        print(f"[PASS] {name}")
    else:
        failed += 1
        print(f"[FAIL] {name}")


def api(path, token=None, method="GET"):
    try:
        return 200, t.request(method, path, token)
    except HTTPError as e:
        try:
            body = json.loads(e.read().decode())
        except Exception:
            body = {}

        return e.code, body


print("===== WALLET CORE TEST =====")

customer = t.login("test-user1")
other = t.login("test-user2")
manager = t.login("catalog_manager")


status, _ = api("/wallet/me")
check("TC01 - Anonymous wallet blocked", status == 401)

status, _ = api("/wallet/me/transactions")
check("TC02 - Anonymous history blocked", status == 401)

status, balance = api("/wallet/me", customer)

check(
    "TC03 - Customer can read wallet",
    status == 200 and isinstance(balance, dict)
)

check(
    "TC04 - Balance is non-negative",
    isinstance(balance, dict)
    and isinstance(balance.get("balance"), (int, float))
    and balance["balance"] >= 0
)

check(
    "TC05 - Currency is VND",
    isinstance(balance, dict)
    and balance.get("currency") == "VND"
)

status, history = api(
    "/wallet/me/transactions",
    customer
)

check(
    "TC06 - Customer can read history",
    status == 200 and isinstance(history, list)
)

status, _ = api("/wallet/me", other)

check(
    "TC07 - Second customer can access own wallet",
    status == 200
)

status, attempted = api(
    "/wallet/me?customerId=another-customer",
    customer
)

check(
    "TC08 - Injected customerId cannot change wallet owner",
    status == 200 and attempted == balance
)

status, _ = api("/wallet/me", manager)

check(
    "TC09 - Catalog manager wallet access blocked",
    status == 403
)

status, _ = api(
    "/wallet/me/transactions?page=0",
    customer
)

check("TC10 - Invalid page rejected", status == 400)

status, _ = api(
    "/wallet/me/transactions?pageSize=1000",
    customer
)

check("TC11 - Excessive page size rejected", status == 400)

status, _ = api(
    "/wallet/me",
    customer,
    method="POST"
)

check(
    "TC12 - Public wallet mutation blocked",
    status in (404, 405)
)

print("\n===== RESULT =====")
print(f"PASS: {passed}")
print(f"FAIL: {failed}")
print(f"TOTAL: {passed + failed}")

sys.exit(1 if failed else 0)

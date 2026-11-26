#!/usr/bin/env python3

import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py")
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

print("===== CUSTOMER LOGIN =====")

username = input("Customer [customer]: ").strip()
token = t.login(username or "customer")

print("\n===== MY LICENSES =====")

licenses = t.request(
    "GET",
    "/licenses/me",
    token
)

print("Total licenses:", len(licenses))

if not licenses:
    raise SystemExit("[STOP] No assigned licenses.")

for i, item in enumerate(licenses, 1):
    print(
        f"[{i}]",
        "License:", item["id"],
        "| Order:", item.get("orderId")
    )

choice = input("\nSelect license number [1]: ").strip()

try:
    index = int(choice or "1") - 1

    if not 0 <= index < len(licenses):
        raise ValueError()
except ValueError:
    raise SystemExit("[STOP] Invalid selection.")

license_id = licenses[index]["id"]

print("\n===== REVEAL LICENSE =====")

result = t.request(
    "GET",
    f"/licenses/me/{license_id}/reveal",
    token
)

key = result.get("licenseKey")

if not key:
    raise SystemExit("[FAIL] No license key returned.")

print("[PASS] License revealed successfully.")

if key.startswith("DEMO-ALLOC-"):
    print("Demo key:", key)
else:
    print("Key hidden in terminal for security.")

print("\n[PASS] Customer License Delivery")

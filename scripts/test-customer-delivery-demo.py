#!/usr/bin/env python3

import importlib.util
import base64
import json

from pathlib import Path

path = Path(__file__).with_name(
    "test-license-allocation.py"
)

spec = importlib.util.spec_from_file_location(
    "allocation", path
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

print("===== LOGIN CUSTOMER =====")

username = input("Customer username [customer]: ").strip()
username = username or "customer"

token = t.login(username)

payload = token.split(".")[1]
payload += "=" * (-len(payload) % 4)

claims = json.loads(
    base64.urlsafe_b64decode(payload)
)

t.CUSTOMER_ID = claims["sub"]

roles = claims.get("realm_access", {}).get("roles", [])

if "customer" not in roles:
    raise SystemExit("[STOP] Account is not a Customer.")

print("\n===== LOGIN STAFF =====")

t.MANAGER = t.login("catalog_manager")
t.COORDINATOR = t.login("license_coordinator")

print("\n===== CREATE TEST PRODUCTS =====")

available = t.create_product("delivery-demo", 3)
empty = t.create_product("empty-demo", 0)

print("[OK] Created product with 3 keys")
print("[OK] Created product with 0 keys")

print("\n===== CREATE SIMULATED PAID ORDERS =====")

success_order = t.make_order(available, 1)
empty_order = t.make_order(empty, 1)

print("\n===== WAIT FOR ALLOCATION =====")

success = t.wait_for(
    lambda: t.order_status(success_order) == "COMPLETED"
)

out_of_stock = t.wait_for(
    lambda: t.fulfillment(empty_order) == "OUT_OF_STOCK"
)

print("\n===== RESULTS =====")

t.check(
    "Available order completed",
    success
)

t.check(
    "Exactly one key assigned",
    t.assigned_count(success_order) == 1
)

t.check(
    "Remaining inventory = 2",
    t.available_count(available) == 2
)

t.check(
    "Empty order detected",
    out_of_stock
)

t.check(
    "No key assigned to empty order",
    t.assigned_count(empty_order) == 0
)

t.check(
    "Empty order remains PAID",
    t.order_status(empty_order) == "PAID"
)

print("\n===== DEMO INFORMATION =====")
print("Customer:", username)
print("Success order:", success_order)
print("Empty order:", empty_order)
print("Success product:", available)
print("Empty product:", empty)

print(f"\nPASSED: {t.PASSED}/6")
print(f"FAILED: {t.FAILED}")

raise SystemExit(1 if t.FAILED else 0)

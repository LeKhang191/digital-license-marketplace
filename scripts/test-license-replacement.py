#!/usr/bin/env python3

import base64
import json
import uuid
import importlib.util

from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py")
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

passed = failed = 0


def call(method, path, token=None, data=None):
    headers = {"Accept": "application/json"}

    if token:
        headers["Authorization"] = f"Bearer {token}"

    body = None

    if data is not None:
        headers["Content-Type"] = "application/json"
        body = json.dumps(data).encode()

    req = Request(
        t.API + path,
        data=body,
        headers=headers,
        method=method
    )

    try:
        with urlopen(req, timeout=30) as response:
            raw = response.read()
            return (
                response.status,
                json.loads(raw) if raw else None
            )

    except HTTPError as e:
        raw = e.read()

        try:
            result = json.loads(raw)
        except (ValueError, UnicodeError):
            result = None

        return e.code, result


def check(name, condition):
    global passed, failed

    if condition:
        passed += 1
        print("[PASS]", name, flush=True)
    else:
        failed += 1
        print("[FAIL]", name, flush=True)


def require(condition, message):
    if not condition:
        raise SystemExit("[STOP] " + message)


def subject(token):
    payload = token.split(".")[1]
    payload += "=" * (-len(payload) % 4)

    return json.loads(
        base64.urlsafe_b64decode(payload)
    )["sub"]


def stock(product):
    status, data = call(
        "GET",
        f"/licenses/availability/{product}"
    )

    require(
        status == 200,
        f"Inventory API returned HTTP {status}"
    )

    return data["available"]


def owned(order):
    status, data = call(
        "GET",
        f"/licenses/me?orderId={order}",
        customer
    )

    require(
        status == 200,
        f"Customer license API returned HTTP {status}"
    )

    return data


def make_ticket(order, license_id):
    status, data = call(
        "POST",
        "/support/tickets",
        customer,
        {
            "orderId": order,
            "licenseId": license_id,
            "category": "INVALID_KEY",
            "subject": "TEST - License replacement",
            "description": "Development replacement test."
        }
    )

    require(
        status == 201 and isinstance(data, dict),
        f"Ticket creation failed: HTTP {status}"
    )

    return data["id"]


def transition(ticket, token, state):
    return call(
        "PATCH",
        f"/support/tickets/{ticket}/status",
        token,
        {"status": state}
    )


def replace(ticket, token):
    return call(
        "POST",
        f"/licenses/replacements/{ticket}",
        token
    )


def reveal(license_id, token):
    return call(
        "GET",
        f"/licenses/me/{license_id}/reveal",
        token
    )


def db_status(license_id):
    return t.sql(
        "license_db",
        f"""
        SELECT "Status"
        FROM license_keys
        WHERE "Id" = {t.literal(license_id)};
        """
    )


def history_count(ticket):
    return int(t.sql(
        "license_db",
        f"""
        SELECT COUNT(*)
        FROM license_replacements
        WHERE "TicketId" = {t.literal(ticket)};
        """
    ))


print("===== LOGIN =====")

customer = t.login("customer")
other = t.login("customer2")

t.MANAGER = t.login("catalog_manager")
t.COORDINATOR = t.login("license_coordinator")

staff = t.COORDINATOR
auditor = t.login("transaction_auditor")

t.CUSTOMER_ID = subject(customer)

require(
    subject(customer) != subject(other),
    "Customer accounts must be different."
)

print("\n===== CREATE DEMO FIXTURES =====")

product = t.create_product("replacement-stock", 3)
empty_product = t.create_product("replacement-empty", 1)

order = t.make_order(product, 1)
empty_order = t.make_order(empty_product, 1)

print("Waiting for license allocation...")

require(
    t.wait_for(
        lambda: t.order_status(order) == "COMPLETED"
    ),
    "First allocation did not complete."
)

require(
    t.wait_for(
        lambda: t.order_status(empty_order) == "COMPLETED"
    ),
    "Second allocation did not complete."
)

licenses = owned(order)
empty_licenses = owned(empty_order)

require(
    len(licenses) == 1,
    "Unexpected delivered licenses."
)

require(
    len(empty_licenses) == 1,
    "Unexpected empty fixture."
)

old = licenses[0]
empty_old = empty_licenses[0]

ticket = make_ticket(order, old["id"])
empty_ticket = make_ticket(empty_order, empty_old["id"])

print("Demo ticket:", ticket)
print("Out-of-stock ticket:", empty_ticket)

print("\n===== AUTHORIZATION =====")

# TC01
status, _ = replace(ticket, None)
check("TC01 - Anonymous rejected", status == 401)

# TC02
status, _ = replace(ticket, customer)
check("TC02 - Customer cannot replace", status == 403)

# TC03
status, _ = replace(ticket, auditor)
check("TC03 - Auditor cannot replace", status == 403)

# TC04
status, _ = replace(ticket, t.MANAGER)
check("TC04 - Catalog manager cannot replace", status == 403)

# TC05
status, _ = replace(str(uuid.uuid4()), staff)
check("TC05 - Unknown ticket rejected", status == 404)

# TC06
status, _ = replace(ticket, staff)
check(
    "TC06 - OPEN ticket not accepted",
    status in (404, 409)
)

status, _ = transition(ticket, staff, "IN_PROGRESS")

require(
    status == 200,
    f"Cannot accept replacement ticket: HTTP {status}"
)

before = stock(product)

require(
    before == 2,
    "Unexpected starting inventory."
)

status, old_key = reveal(old["id"], customer)

require(
    status == 200 and isinstance(old_key, dict),
    "Original key unavailable before replacement."
)

require(
    str(old_key.get("licenseKey", "")).startswith("DEMO-ALLOC-"),
    "Refusing to replace a non-demo key."
)

print("\n===== SUCCESSFUL REPLACEMENT =====")

# TC07
status, result = replace(ticket, staff)

check(
    "TC07 - Replacement succeeds",
    status == 200 and
    isinstance(result, dict) and
    result.get("alreadyProcessed") is False
)

require(
    status == 200 and
    isinstance(result, dict) and
    result.get("newLicenseId"),
    f"Replacement failed: HTTP {status}"
)

new_id = result["newLicenseId"]

# TC08
check(
    "TC08 - A different license is assigned",
    new_id != old["id"]
)

# TC09
check(
    "TC09 - Original license is disabled",
    db_status(old["id"]) == "DISABLED"
)

# TC10
check(
    "TC10 - New license is assigned",
    db_status(new_id) == "ASSIGNED"
)

# TC11
check(
    "TC11 - Exactly one stock unit consumed",
    stock(product) == before - 1
)

# TC12
check(
    "TC12 - Replacement history recorded",
    history_count(ticket) == 1
)

current = owned(order)

# TC13
check(
    "TC13 - Customer sees replacement only",
    len(current) == 1 and
    current[0]["id"] == new_id
)

# TC14
check(
    "TC14 - Product and order item preserved",
    len(current) == 1 and
    current[0]["productId"] == old["productId"] and
    current[0]["orderItemId"] == old["orderItemId"]
)

# TC15
status, _ = reveal(old["id"], customer)

check(
    "TC15 - Old key reveal blocked",
    status == 404
)

# TC16
status, new_key = reveal(new_id, customer)

check(
    "TC16 - Owner receives a new key",
    status == 200 and
    isinstance(new_key, dict) and
    bool(new_key.get("licenseKey")) and
    new_key["licenseKey"] != old_key["licenseKey"]
)

del old_key, new_key

# TC17
status, _ = reveal(new_id, other)

check(
    "TC17 - Other customer cannot reveal new key",
    status == 404
)

print("\n===== IDEMPOTENCY =====")

# TC18
status, retry = replace(ticket, staff)

check(
    "TC18 - Retry returns the same replacement",
    status == 200 and
    isinstance(retry, dict) and
    retry.get("alreadyProcessed") is True and
    retry.get("newLicenseId") == new_id
)

# TC19
check(
    "TC19 - Retry does not consume more stock",
    stock(product) == before - 1
)

# TC20
check(
    "TC20 - Retry does not duplicate history",
    history_count(ticket) == 1
)

print("\n===== OUT OF STOCK =====")

status, _ = transition(
    empty_ticket, staff, "IN_PROGRESS"
)

require(
    status == 200,
    f"Cannot accept empty ticket: HTTP {status}"
)

require(
    stock(empty_product) == 0,
    "Expected zero replacement inventory."
)

# TC21
status, error = replace(empty_ticket, staff)

check(
    "TC21 - Replacement out of stock rejected",
    status == 409 and
    isinstance(error, dict) and
    error.get("code") == "REPLACEMENT_OUT_OF_STOCK"
)

# TC22
check(
    "TC22 - Original license remains assigned",
    db_status(empty_old["id"]) == "ASSIGNED"
)

# TC23
check(
    "TC23 - Failed replacement leaves no history",
    history_count(empty_ticket) == 0
)

# TC24
current = owned(empty_order)

check(
    "TC24 - Customer retains original license",
    len(current) == 1 and
    current[0]["id"] == empty_old["id"]
)

print("\n===== CLOSE SUCCESSFUL TICKET =====")

# TC25
status, _ = transition(ticket, staff, "RESOLVED")

check(
    "TC25 - Staff resolves ticket",
    status == 200
)

# TC26
status, _ = transition(ticket, customer, "CLOSED")

check(
    "TC26 - Customer closes ticket",
    status == 200
)

# TC27
check(
    "TC27 - Order remains COMPLETED",
    t.order_status(order) == "COMPLETED"
)

print(
    "\nOut-of-stock ticket intentionally left IN_PROGRESS:",
    empty_ticket
)

print("\n===== FINAL RESULTS =====")
print(f"PASSED: {passed}")
print(f"FAILED: {failed}")
print(f"TOTAL : {passed + failed}")

raise SystemExit(
    0 if passed == 27 and failed == 0 else 1
)

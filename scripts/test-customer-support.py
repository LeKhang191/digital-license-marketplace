#!/usr/bin/env python3

import importlib.util
import uuid
from pathlib import Path
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py")
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

passed = failed = 0


def call(method, path, token=None, data=None):
    try:
        result = t.request(method, path, token, data)
        return (201 if method == "POST" and
                path == "/support/tickets" else 200), result
    except HTTPError as e:
        return e.code, None


def check(name, condition):
    global passed, failed

    if condition:
        passed += 1
        print("[PASS]", name)
    else:
        failed += 1
        print("[FAIL]", name)


print("===== LOGIN =====")

customer = t.login("customer")
other = t.login("customer2")
coordinator = t.login("license_coordinator")
auditor = t.login("transaction_auditor")

licenses = t.request("GET", "/licenses/me", customer)

if not licenses:
    raise SystemExit(
        "[STOP] Customer has no delivered license."
    )

license = licenses[0]

base = "/support/tickets"

payload = {
    "orderId": license["orderId"],
    "licenseId": license["id"],
    "category": "INVALID_KEY",
    "subject": "TEST - Invalid demo key",
    "description": "Development support workflow test."
}

print("\n===== CREATE AND OWNERSHIP =====")

# TC01
status, _ = call("GET", base)
check(
    "TC01 - Anonymous access blocked",
    status == 401
)

# TC02
status, _ = call("POST", base, other, payload)
check(
    "TC02 - Foreign order blocked",
    status == 404
)

# TC03
status, _ = call(
    "POST", base, customer,
    {**payload, "licenseId": str(uuid.uuid4())}
)
check(
    "TC03 - Invalid license blocked",
    status == 404
)

# TC04
status, _ = call(
    "POST", base, customer,
    {**payload, "licenseId": None}
)
check(
    "TC04 - Required license validated",
    status == 400
)

# TC05
status, _ = call(
    "POST", base, customer,
    {**payload, "subject": ""}
)
check(
    "TC05 - Empty subject blocked",
    status == 400
)

# TC06
status, ticket = call("POST", base, customer, payload)

check(
    "TC06 - Customer creates ticket",
    status == 201 and
    isinstance(ticket, dict) and
    ticket.get("status") == "OPEN"
)

if not isinstance(ticket, dict) or not ticket.get("id"):
    raise SystemExit(
        "[STOP] Cannot continue. Check existing active tickets."
    )

tid = ticket["id"]
path = f"{base}/{tid}"

print("Ticket ID:", tid)

# TC07
check(
    "TC07 - Correct assigned role",
    ticket.get("assignedRole") == "license_coordinator"
)

# TC08
status, _ = call("POST", base, customer, payload)
check(
    "TC08 - Duplicate ticket blocked",
    status == 409
)

# TC09
status, _ = call("GET", path, customer)
check(
    "TC09 - Customer views own ticket",
    status == 200
)

# TC10
status, _ = call("GET", path, other)
check(
    "TC10 - Foreign ticket blocked",
    status == 404
)

# TC11
status, _ = call("GET", path, auditor)
check(
    "TC11 - Wrong staff role blocked",
    status == 404
)

print("\n===== SUPPORT WORKFLOW =====")

# TC12
status, _ = call(
    "PATCH", path + "/status", customer,
    {"status": "RESOLVED"}
)
check(
    "TC12 - Customer cannot resolve ticket",
    status == 403
)

# TC13
status, _ = call(
    "PATCH", path + "/status", coordinator,
    {"status": "RESOLVED"}
)
check(
    "TC13 - Cannot skip processing",
    status == 409
)

# TC14
status, _ = call(
    "PATCH", path + "/status", coordinator,
    {"status": "IN_PROGRESS"}
)
check(
    "TC14 - Staff accepts ticket",
    status == 200
)

# TC15
status, _ = call(
    "POST", path + "/messages", customer,
    {"content": "This is a test message from customer."}
)
check(
    "TC15 - Customer sends message",
    status == 200
)

# TC16
status, _ = call(
    "POST", path + "/messages", coordinator,
    {"content": "This is a test response from staff."}
)
check(
    "TC16 - Staff sends message",
    status == 200
)

# TC17
status, _ = call(
    "POST", path + "/messages", auditor,
    {"content": "Unauthorized response"}
)
check(
    "TC17 - Wrong staff cannot reply",
    status == 404
)

# TC18
status, _ = call(
    "POST", path + "/messages", customer,
    {"content": " "}
)
check(
    "TC18 - Empty message blocked",
    status == 400
)

# TC19
status, _ = call(
    "PATCH", path + "/status", coordinator,
    {"status": "RESOLVED"}
)
check(
    "TC19 - Staff resolves ticket",
    status == 200
)

# TC20
status, _ = call(
    "PATCH", path + "/status", customer,
    {"status": "CLOSED"}
)
check(
    "TC20 - Customer closes ticket",
    status == 200
)

# TC21
status, _ = call(
    "POST", path + "/messages", customer,
    {"content": "Message after close"}
)
check(
    "TC21 - Closed ticket rejects messages",
    status == 409
)

# TC22
status, detail = call("GET", path, customer)

check(
    "TC22 - Support history preserved",
    status == 200 and
    isinstance(detail, dict) and
    detail.get("status") == "CLOSED" and
    len(detail.get("messages", [])) == 2
)

# TC23
status, data = call("GET", base, other)

check(
    "TC23 - Other customer list excludes ticket",
    status == 200 and
    isinstance(data, dict) and
    all(x["id"] != tid for x in data.get("items", []))
)

print("\n===== FINAL RESULTS =====")
print(f"PASSED: {passed}")
print(f"FAILED: {failed}")
print(f"TOTAL : {passed + failed}")

raise SystemExit(
    0 if passed == 23 and failed == 0 else 1
)

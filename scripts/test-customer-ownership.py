#!/usr/bin/env python3

import importlib.util
import base64
import json
import uuid

from pathlib import Path
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name(
        "test-license-allocation.py"
    )
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

PASSED = 0
FAILED = 0


def check(name, condition):
    global PASSED, FAILED

    if condition:
        PASSED += 1
        print(f"[PASS] {name}")
    else:
        FAILED += 1
        print(f"[FAIL] {name}")


def claims(token):
    payload = token.split(".")[1]
    payload += "=" * (-len(payload) % 4)

    return json.loads(
        base64.urlsafe_b64decode(payload)
    )


def request(path, token=None):
    try:
        result = t.request("GET", path, token)
        return 200, result

    except HTTPError as e:
        return e.code, None


def login_customer(username):
    token = t.login(username)
    data = claims(token)

    roles = data.get(
        "realm_access", {}
    ).get("roles", [])

    if "customer" not in roles:
        raise SystemExit(
            f"[STOP] {username} is not a Customer."
        )

    return token, data["sub"]


print("===== CUSTOMER A =====")

username_a = input(
    "Username [customer]: "
).strip() or "customer"

token_a, subject_a = login_customer(username_a)

status, licenses_a = request(
    "/licenses/me", token_a
)

if status != 200 or not licenses_a:
    raise SystemExit(
        "[STOP] Customer A must have assigned licenses."
    )

target = licenses_a[0]

license_id = target["id"]
order_id = target["orderId"]

print("Target license:", license_id)
print("Target order:", order_id)

print("\n===== CUSTOMER B =====")

username_b = input(
    "Username [customer2]: "
).strip() or "customer2"

token_b, subject_b = login_customer(username_b)

if subject_a == subject_b:
    raise SystemExit(
        "[STOP] A and B must be different accounts."
    )

print("\n===== OWNERSHIP TESTS =====")

# TC01: Owner can list own licenses.
check(
    "TC01 - Owner can list licenses",
    any(x["id"] == license_id for x in licenses_a)
)

# TC02: Owner can reveal own key.
status, result = request(
    f"/licenses/me/{license_id}/reveal",
    token_a
)

check(
    "TC02 - Owner can reveal key",
    status == 200
    and isinstance(result, dict)
    and bool(result.get("licenseKey"))
)

# Do not print or retain plaintext keys.
del result

# TC03: Customer B can list own licenses.
status, licenses_b = request(
    "/licenses/me",
    token_b
)

check(
    "TC03 - Customer B can list licenses",
    status == 200
    and isinstance(licenses_b, list)
)

# TC04: Customer B cannot see Customer A's license.
check(
    "TC04 - Foreign license absent from list",
    status == 200
    and isinstance(licenses_b, list)
    and all(
        x["id"] != license_id
        for x in licenses_b
    )
)

# TC05: Customer B cannot reveal A's key.
status, _ = request(
    f"/licenses/me/{license_id}/reveal",
    token_b
)

check(
    "TC05 - Foreign license reveal blocked",
    status == 404
)

# TC06: B cannot retrieve A's licenses by order ID.
status, result = request(
    f"/licenses/me?orderId={order_id}",
    token_b
)

check(
    "TC06 - Foreign order filter blocked",
    status == 200 and result == []
)

# TC07: Owner can filter own order.
status, result = request(
    f"/licenses/me?orderId={order_id}",
    token_a
)

check(
    "TC07 - Owner order filter works",
    status == 200
    and isinstance(result, list)
    and any(x["id"] == license_id for x in result)
    and all(x["orderId"] == order_id for x in result)
)

# TC08: Unknown license ID.
status, _ = request(
    f"/licenses/me/{uuid.uuid4()}/reveal",
    token_a
)

check(
    "TC08 - Unknown license rejected",
    status == 404
)

# TC09: Invalid JWT.
status, _ = request(
    f"/licenses/me/{license_id}/reveal",
    "invalid-token"
)

check(
    "TC09 - Invalid token rejected",
    status == 401
)

# TC10: List API must not expose key material.
def contains_secret(value):
    forbidden = {
        "licensekey",
        "encryptedkey",
        "keyfingerprint",
        "plaintextkey",
        "rawkey"
    }

    if isinstance(value, dict):
        return any(
            k.lower() in forbidden
            or contains_secret(v)
            for k, v in value.items()
        )

    if isinstance(value, list):
        return any(
            contains_secret(x)
            for x in value
        )

    return False


check(
    "TC10 - No secret fields in license list",
    not contains_secret(licenses_a)
)

# TC11: Owner can still access key after B's attempts.
status, result = request(
    f"/licenses/me/{license_id}/reveal",
    token_a
)

check(
    "TC11 - Owner access remains intact",
    status == 200
    and isinstance(result, dict)
    and bool(result.get("licenseKey"))
)

del result

print("\n===== ROLE VALIDATION =====")

staff = [
    "catalog_manager",
    "license_coordinator",
    "transaction_auditor"
]

for index, username in enumerate(staff, 12):
    print(f"\nLogin: {username}")

    token = t.login(username)

    roles = claims(token).get(
        "realm_access", {}
    ).get("roles", [])

    if "customer" in roles:
        print(
            f"[WARN] {username} also has customer role. "
            "Checking actual API authorization."
        )

    list_status, _ = request(
        "/licenses/me", token
    )

    reveal_status, _ = request(
        f"/licenses/me/{license_id}/reveal",
        token
    )

    check(
        f"TC{index} - {username} access blocked",
        list_status == 403
        and reveal_status == 403
    )

print("\n===== FINAL RESULTS =====")
print(f"PASSED: {PASSED}")
print(f"FAILED: {FAILED}")
print(f"TOTAL : {PASSED + FAILED}")

raise SystemExit(
    0 if PASSED == 14 and FAILED == 0 else 1
)

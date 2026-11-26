#!/usr/bin/env python3

import importlib.util
import json
import threading
import os

from pathlib import Path
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from urllib.request import Request, urlopen
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location(
    "allocation",
    Path(__file__).with_name("test-license-allocation.py")
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

BASE = os.environ.get(
    "SUPPORT_TEST_API",
    "http://127.0.0.1:8000/api"
).rstrip("/")

WORKERS = 8
passed = failed = 0


def call(method, path, token, data=None):
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/json"
    }

    body = None

    if data is not None:
        headers["Content-Type"] = "application/json"
        body = json.dumps(data).encode()

    req = Request(
        BASE + path,
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
        print("[PASS]", name)
    else:
        failed += 1
        print("[FAIL]", name)


def parallel(method, path, token, data):
    barrier = threading.Barrier(WORKERS)

    def worker(index):
        barrier.wait(timeout=15)
        return call(method, path, token, data)

    with ThreadPoolExecutor(
        max_workers=WORKERS
    ) as executor:
        results = list(
            executor.map(worker, range(WORKERS))
        )

    counts = Counter(status for status, _ in results)

    print("HTTP distribution:", dict(counts))

    return results, counts


print("===== LOGIN =====")

customer = t.login("test-user1")
staff = t.login("license_coordinator")

status, licenses = call(
    "GET", "/licenses/me", customer
)

if status != 200 or not licenses:
    raise SystemExit("[STOP] No delivered license.")

license = licenses[0]
base = "/support/tickets"

payload = {
    "orderId": license["orderId"],
    "licenseId": license["id"],
    "category": "INVALID_KEY",
    "subject": "TEST - Support concurrency",
    "description": "Development concurrency testing."
}

print("\n===== PARALLEL CREATE =====")

results, counts = parallel(
    "POST", base, customer, payload
)

check(
    "TC01 - Exactly one ticket created",
    counts[201] == 1
)

check(
    "TC02 - Remaining requests rejected",
    counts[409] == WORKERS - 1
)

created = [
    body for status, body in results
    if status == 201
    and isinstance(body, dict)
    and body.get("id")
]

if len(created) != 1:
    raise SystemExit(
        "[STOP] Unexpected create results. "
        "Check active tickets before retrying."
    )

tid = created[0]["id"]
path = f"{base}/{tid}"

print("Ticket ID:", tid)

status, ticket = call(
    "GET", path, customer
)

check(
    "TC03 - Created ticket is OPEN",
    status == 200
    and ticket.get("status") == "OPEN"
)

print("\n===== PARALLEL ACCEPT =====")

results, counts = parallel(
    "PATCH",
    path + "/status",
    staff,
    {"status": "IN_PROGRESS"}
)

check(
    "TC04 - Exactly one accept succeeds",
    counts[200] == 1
    and counts[409] == WORKERS - 1
)

status, ticket = call(
    "GET", path, customer
)

check(
    "TC05 - Ticket accepted and assigned",
    status == 200
    and ticket.get("status") == "IN_PROGRESS"
    and bool(ticket.get("handledBy"))
)

print("\n===== PARALLEL RESOLVE =====")

results, counts = parallel(
    "PATCH",
    path + "/status",
    staff,
    {"status": "RESOLVED"}
)

check(
    "TC06 - Exactly one resolve succeeds",
    counts[200] == 1
    and counts[409] == WORKERS - 1
)

status, ticket = call(
    "GET", path, customer
)

check(
    "TC07 - Final status is RESOLVED",
    status == 200
    and ticket.get("status") == "RESOLVED"
)

print("\n===== CLOSE TEST TICKET =====")

status, _ = call(
    "PATCH",
    path + "/status",
    customer,
    {"status": "CLOSED"}
)

detail_status, ticket = call(
    "GET", path, customer
)

check(
    "TC08 - Test ticket closed successfully",
    status == 200
    and detail_status == 200
    and ticket.get("status") == "CLOSED"
)

print("\n===== FINAL RESULTS =====")
print(f"PASSED: {passed}")
print(f"FAILED: {failed}")
print(f"TOTAL : {passed + failed}")

raise SystemExit(
    0 if passed == 8 and failed == 0 else 1
)

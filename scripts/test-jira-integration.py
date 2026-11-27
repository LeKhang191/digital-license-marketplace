import importlib.util
import uuid
from concurrent.futures import ThreadPoolExecutor
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
        status = 201 if (
            method == "POST" and path == "/support/tickets"
        ) else 200
        return status, result
    except HTTPError as e:
        return e.code, None

def check(name, condition):
    global passed, failed
    if condition:
        passed += 1
        print("[PASS]", name, flush=True)
    else:
        failed += 1
        print("[FAIL]", name, flush=True)

print("===== LOGIN =====")

customer = t.login("test-user1")
other = t.login("test-user2")
coordinator = t.login("license_coordinator")
auditor = t.login("transaction_auditor")

licenses = t.request("GET", "/licenses/me", customer)

if not licenses:
    raise SystemExit("[STOP] No delivered test license")

print("\n===== CREATE TEST TICKET =====")

ticket = None

for license in licenses:
    status, result = call(
        "POST", "/support/tickets", customer,
        {
            "orderId": license["orderId"],
            "licenseId": license["id"],
            "category": "INVALID_KEY",
            "subject": f"JIRA TEST {uuid.uuid4().hex[:8]}",
            "description": "Synthetic Jira integration test"
        }
    )

    if status == 201:
        ticket = result
        break

    if status != 409:
        raise SystemExit(f"[STOP] Create ticket HTTP {status}")

if not ticket:
    raise SystemExit("[STOP] No available test ticket")

tid = ticket["id"]
base = f"/support/tickets/{tid}"

print("Ticket ID:", tid)

check("TC01 - Create ticket",
      ticket.get("status") == "OPEN")

print("\n===== ACCESS CONTROL =====")

status, _ = call("POST", base + "/escalate")
check("TC02 - Anonymous blocked", status == 401)

status, _ = call(
    "POST", base + "/escalate", customer
)
check("TC03 - Customer blocked", status == 403)

status, _ = call(
    "POST", base + "/escalate", other
)
check("TC04 - Other customer blocked", status == 403)

status, _ = call(
    "POST", base + "/escalate", auditor
)
check("TC05 - Wrong staff role blocked", status == 404)

status, _ = call(
    "POST", base + "/escalate", coordinator
)
check("TC06 - Unassigned staff blocked", status == 404)

status, _ = call(
    "PATCH", base + "/status", coordinator,
    {"status": "IN_PROGRESS"}
)
check("TC07 - Staff accepts ticket", status == 200)

if status != 200:
    raise SystemExit("[STOP] Cannot accept ticket")

status, _ = call(
    "GET", base + "/external", coordinator
)
check("TC08 - No external link yet", status == 404)

print("\n===== CONCURRENT ESCALATION =====")

def escalate(_):
    return call(
        "POST", base + "/escalate", coordinator
    )

with ThreadPoolExecutor(max_workers=3) as pool:
    results = list(pool.map(escalate, range(3)))

print("HTTP statuses:", [r[0] for r in results])

check("TC09 - Concurrent requests handled",
      all(r[0] in (200, 409) for r in results))

status, link = call(
    "GET", base + "/external", coordinator
)

linked = (
    status == 200
    and isinstance(link, dict)
    and link.get("state") == "LINKED"
    and bool(link.get("issueId"))
    and bool(link.get("issueKey"))
)

check("TC10 - Jira ticket linked", linked)

if not linked:
    raise SystemExit(
        "[STOP] Link not established. Do not retry blindly."
    )

issue_id = link["issueId"]
issue_key = link["issueKey"]

print("Jira Issue ID:", issue_id)
print("Jira Issue Key:", issue_key)

successful = [
    data for status, data in results
    if status == 200 and isinstance(data, dict)
]

check("TC11 - Same Jira issue across successful requests",
      bool(successful) and all(
          x.get("issueId") == issue_id
          for x in successful
      ))

print("\n===== IDEMPOTENCY =====")

status, repeat = call(
    "POST", base + "/escalate", coordinator
)

check("TC12 - Repeat returns existing issue",
      status == 200
      and repeat.get("issueId") == issue_id)

count = t.sql(
    "support_db",
    'SELECT count(*) FROM support_external_links '
    'WHERE "TicketId" = ' + t.literal(tid)
)

check("TC13 - Exactly one database link", count == "1")

print("\n===== SYNC AND SECURITY =====")

status, _ = call(
    "GET", base + "/external", customer
)
check("TC14 - Customer cannot read Jira link",
      status == 403)

status, _ = call(
    "POST", base + "/external/sync", auditor
)
check("TC15 - Wrong staff cannot sync", status == 404)

status, synced = call(
    "POST", base + "/external/sync", coordinator
)

check("TC16 - Sync Jira status",
      status == 200
      and synced.get("issueId") == issue_id
      and bool(synced.get("externalStatus")))

check("TC17 - Sync comments and timestamp",
      status == 200
      and isinstance(synced.get("comments"), list)
      and bool(synced.get("syncedAtUtc")))

status, _ = call(
    "POST", base + "/external/reconcile", coordinator,
    {"issueKey": issue_key}
)
check("TC18 - Linked ticket rejects reconcile",
      status == 409)

status, detail = call("GET", base, customer)

check("TC19 - Business status unchanged",
      status == 200
      and detail.get("status") == "IN_PROGRESS")

print("\n===== FINAL RESULTS =====")
print("SUPPORT_TICKET_ID=" + tid)
print("JIRA_ISSUE_KEY=" + issue_key)
print("PASSED:", passed)
print("FAILED:", failed)
print("TOTAL :", passed + failed)

raise SystemExit(0 if passed == 19 and failed == 0 else 1)

#!/usr/bin/env python3

import threading
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

# Reuse helpers from the passing business test.
source = Path(__file__) if False else None

from pathlib import Path

source = Path(__file__).with_name(
    "test-license-replacement.py"
)

code = source.read_text()
marker = 'print("===== LOGIN =====")'

if code.count(marker) != 1:
    raise SystemExit("[STOP] Unexpected helper structure.")

exec(
    compile(
        code.split(marker, 1)[0],
        str(source),
        "exec"
    ),
    globals()
)

WORKERS = 8

print("===== LOGIN =====")

customer = t.login("customer")

t.MANAGER = t.login("catalog_manager")
t.COORDINATOR = t.login("license_coordinator")

staff = t.COORDINATOR
t.CUSTOMER_ID = subject(customer)


def fixture(name):
    product = t.create_product(name, 3)
    order = t.make_order(product, 1)

    require(
        t.wait_for(
            lambda: t.order_status(order) == "COMPLETED"
        ),
        "Demo allocation did not complete."
    )

    licenses = owned(order)

    require(
        len(licenses) == 1,
        "Unexpected license count."
    )

    old = licenses[0]

    status, key = reveal(old["id"], customer)

    require(
        status == 200
        and isinstance(key, dict)
        and str(key.get("licenseKey", "")).startswith(
            "DEMO-ALLOC-"
        ),
        "Refusing to test a non-demo key."
    )

    del key

    ticket = make_ticket(order, old["id"])

    status, _ = transition(
        ticket, staff, "IN_PROGRESS"
    )

    require(
        status == 200,
        "Cannot accept demo ticket."
    )

    return product, order, old, ticket


def close_ticket(ticket):
    status, _ = transition(
        ticket, staff, "RESOLVED"
    )

    require(status == 200, "Cannot resolve ticket.")

    status, _ = transition(
        ticket, customer, "CLOSED"
    )

    require(status == 200, "Cannot close ticket.")


print("\n===== CONCURRENCY FIXTURE =====")

product, order, old, ticket = fixture(
    "replacement-concurrency"
)

before = stock(product)

require(before == 2, "Unexpected starting stock.")

barrier = threading.Barrier(WORKERS)


def worker(index):
    barrier.wait(timeout=15)
    return replace(ticket, staff)


print("\n===== PARALLEL REPLACEMENT =====")

with ThreadPoolExecutor(
    max_workers=WORKERS
) as executor:
    results = list(
        executor.map(worker, range(WORKERS))
    )

counts = Counter(
    status for status, _ in results
)

print("HTTP distribution:", dict(counts))

successes = [
    body for status, body in results
    if status == 200
    and isinstance(body, dict)
    and body.get("newLicenseId")
]

fresh = [
    x for x in successes
    if x.get("alreadyProcessed") is False
]

new_ids = {
    x["newLicenseId"] for x in successes
}

check(
    "TC01 - All parallel requests succeed safely",
    counts[200] == WORKERS
    and len(successes) == WORKERS
)

check(
    "TC02 - Exactly one new replacement",
    len(fresh) == 1
)

check(
    "TC03 - All requests return the same new license",
    len(successes) == WORKERS
    and len(new_ids) == 1
)

require(
    len(new_ids) == 1,
    "Unexpected replacement result."
)

new_id = next(iter(new_ids))

check(
    "TC04 - Exactly one stock unit consumed",
    stock(product) == before - 1
)

check(
    "TC05 - Exactly one history record",
    history_count(ticket) == 1
)

check(
    "TC06 - Correct license states",
    db_status(old["id"]) == "DISABLED"
    and db_status(new_id) == "ASSIGNED"
)

current = owned(order)

check(
    "TC07 - Customer receives exactly one new license",
    len(current) == 1
    and current[0]["id"] == new_id
    and current[0]["orderItemId"] == old["orderItemId"]
)

close_ticket(ticket)


print("\n===== ROLLBACK FIXTURE =====")

product, order, old, ticket = fixture(
    "replacement-rollback"
)

before = stock(product)

require(before == 2, "Unexpected rollback stock.")

suffix = uuid.uuid4().hex[:12]

function_name = f"test_replacement_fail_{suffix}"
trigger_name = f"test_replacement_trigger_{suffix}"

print("Rollback ticket:", ticket)
print("Temporary trigger:", trigger_name)

cleanup = f"""
DROP TRIGGER IF EXISTS {trigger_name}
ON license_replacements;

DROP FUNCTION IF EXISTS {function_name}();
"""

try:
    # Inject a failure only for this specific test ticket.
    t.sql(
        "license_db",
        f"""
        CREATE FUNCTION {function_name}()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            IF NEW."TicketId" = '{ticket}'::uuid THEN
                RAISE EXCEPTION
                    'TEST_ONLY: replacement rollback';
            END IF;

            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER {trigger_name}
        BEFORE INSERT ON license_replacements
        FOR EACH ROW
        EXECUTE FUNCTION {function_name}();
        """
    )

    print("\n===== INJECTED DATABASE FAILURE =====")

    status, error = replace(ticket, staff)

    print("Injected failure HTTP:", status)

    check(
        "TC08 - Injected database failure rejected",
        status == 500
    )

    current = owned(order)

    check(
        "TC09 - Old license restored by rollback",
        db_status(old["id"]) == "ASSIGNED"
        and len(current) == 1
        and current[0]["id"] == old["id"]
        and current[0]["orderItemId"] == old["orderItemId"]
    )

    check(
        "TC10 - Inventory preserved after rollback",
        stock(product) == before
    )

    check(
        "TC11 - No failed replacement history",
        history_count(ticket) == 0
    )

finally:
    t.sql("license_db", cleanup)
    print("[OK] Temporary failure trigger removed.")


print("\n===== RETRY AFTER ROLLBACK =====")

status, result = replace(ticket, staff)

check(
    "TC12 - Retry succeeds after removing failure",
    status == 200
    and isinstance(result, dict)
    and result.get("alreadyProcessed") is False
    and stock(product) == before - 1
    and history_count(ticket) == 1
    and db_status(old["id"]) == "DISABLED"
    and db_status(result["newLicenseId"]) == "ASSIGNED"
)

if status == 200:
    close_ticket(ticket)

print("\n===== FINAL RESULTS =====")
print(f"PASSED: {passed}")
print(f"FAILED: {failed}")
print(f"TOTAL : {passed + failed}")

raise SystemExit(
    0 if passed == 12 and failed == 0 else 1
)

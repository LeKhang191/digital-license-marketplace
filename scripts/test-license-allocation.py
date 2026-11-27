
#!/usr/bin/env python3

import getpass
import json
import subprocess
import time
import uuid

from datetime import datetime, timezone
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import urlencode
from urllib.error import HTTPError


ROOT = Path(__file__).resolve().parent.parent

API = "http://localhost:8000/api"
KEYCLOAK = "http://localhost:8081"

RUN_ID = uuid.uuid4().hex[:12]

CUSTOMER_ID = f"allocation-test-{RUN_ID}"

PASSED = 0
FAILED = 0

TEST_PRODUCTS = []
TEST_ORDERS = []


def run(args, stdin=None):
    result = subprocess.run(
        args,
        cwd=ROOT,
        input=stdin,
        text=True,
        capture_output=True,
        check=True,
    )

    return result.stdout.strip()


def sql(database, statement):
    return run([
        "docker", "compose", "exec", "-T",
        "postgres", "psql",
        "-X", "-U", "app",
        "-d", database,
        "-v", "ON_ERROR_STOP=1",
        "-Atq",
    ], statement)


def literal(value):
    return "'" + str(value).replace("'", "''") + "'"


def request(method, path, token=None, data=None):
    headers = {
        "Accept": "application/json",
    }

    if token:
        headers["Authorization"] = f"Bearer {token}"

    body = None

    if data is not None:
        body = json.dumps(data).encode()
        headers["Content-Type"] = "application/json"

    req = Request(
        API + path,
        data=body,
        headers=headers,
        method=method,
    )

    with urlopen(req, timeout=30) as response:
        raw = response.read()

        return json.loads(raw) if raw else None


def login(username):
    password = getpass.getpass(
        f"Password for {username}: "
    )

    body = urlencode({
        "grant_type": "password",
        "client_id": "marketplace-web",
        "username": username,
        "password": password,
    }).encode()

    req = Request(
        KEYCLOAK
        + "/realms/marketplace/protocol/openid-connect/token",
        data=body,
        method="POST",
        headers={
            "Content-Type":
                "application/x-www-form-urlencoded"
        }
    )

    with urlopen(req, timeout=30) as response:
        token = json.load(response)["access_token"]

    return token


def check(name, condition):
    global PASSED, FAILED

    if condition:
        PASSED += 1
        print(f"[PASS] {name}", flush=True)
    else:
        FAILED += 1
        print(f"[FAIL] {name}", flush=True)


def wait_for(fn, timeout=90):
    deadline = time.monotonic() + timeout

    while time.monotonic() < deadline:
        if fn():
            return True

        time.sleep(1)

    return False


def create_product(name, quantity):
    slug = f"allocation-{RUN_ID}-{name}"

    product = request(
        "POST",
        "/catalog/products",
        MANAGER,
        {
            "name": f"TEST Allocation {name} {RUN_ID}",
            "slug": slug,
            "description": "Development test only",
            "category": "Game",
            "price": 100000,
            "isActive": True,
        }
    )

    product_id = str(uuid.UUID(product["id"]))

    TEST_PRODUCTS.append(product_id)

    if quantity:
        keys = [
            f"DEMO-ALLOC-{RUN_ID}-{name}-{i}"
            for i in range(quantity)
        ]

        result = request(
            "POST",
            "/licenses/import",
            COORDINATOR,
            {
                "productId": product_id,
                "keys": keys,
            },
        )

        if result["imported"] != quantity:
            raise RuntimeError(
                "Test inventory import failed"
            )

    return product_id


def make_order(product_id, quantity):
    order_id = str(uuid.uuid4())

    items = [
        {
            "itemId": str(uuid.uuid4()),
            "productId": product_id,
        }
        for _ in range(quantity)
    ]

    paid_at = datetime.now(timezone.utc).isoformat()

    payload = {
        "orderId": order_id,
        "keycloakUserId": CUSTOMER_ID,
        "customerEmail": None,
        "amount": quantity * 100000,
        "items": items,
        "paidAtUtc": paid_at,
    }

    TEST_ORDERS.append(order_id)

    statements = [
        "BEGIN;",
        f"""
        INSERT INTO orders (
            "Id",
            "KeycloakUserId",
            "TotalAmount",
            "Status",
            "CreatedAtUtc",
            "PaidAtUtc"
        )
        VALUES (
            {literal(order_id)},
            {literal(CUSTOMER_ID)},
            {quantity * 100000},
            'PAID',
            now(),
            {literal(paid_at)}
        );
        """,
    ]

    for item in items:
        statements.append(f"""
            INSERT INTO order_items (
                "Id",
                "OrderId",
                "ProductId",
                "Price"
            )
            VALUES (
                {literal(item["itemId"])},
                {literal(order_id)},
                {literal(product_id)},
                100000
            );
        """)

    statements.append(f"""
        INSERT INTO outbox_messages (
            "Id",
            "AggregateId",
            "RoutingKey",
            "Payload",
            "CreatedAtUtc",
            "RetryCount",
            "NextRetryAtUtc"
        )
        VALUES (
            {literal(uuid.uuid4())},
            {literal(order_id)},
            'order.paid',
            {literal(json.dumps(payload))}::jsonb,
            now(),
            0,
            now()
        );

        COMMIT;
    """)

    sql("order_db", "\n".join(statements))

    return order_id


def fulfillment(order_id):
    return sql("order_db", f"""
        SELECT "Status"
        FROM fulfillment_results
        WHERE "OrderId" = {literal(order_id)};
    """)


def order_status(order_id):
    return sql("order_db", f"""
        SELECT "Status"
        FROM orders
        WHERE "Id" = {literal(order_id)};
    """)


def assigned_count(order_id):
    return int(sql("license_db", f"""
        SELECT count(*)
        FROM license_keys
        WHERE "OrderId" = {literal(order_id)}
          AND "Status" = 'ASSIGNED';
    """))


def available_count(product_id):
    return int(sql("license_db", f"""
        SELECT count(*)
        FROM license_keys
        WHERE "ProductId" = {literal(product_id)}
          AND "Status" = 'AVAILABLE';
    """))


def published(database, table, id_column, order_id):
    return sql(database, f"""
        SELECT count(*)
        FROM {table}
        WHERE "{id_column}" = {literal(order_id)}
          AND "PublishedAtUtc" IS NOT NULL;
    """) == "1"



def order_outbox_published(order_id):
    return sql("order_db", f"""
        SELECT CASE
            WHEN count(*) > 0
             AND count(*) FILTER (
                 WHERE "PublishedAtUtc" IS NULL
             ) = 0
            THEN 1 ELSE 0
        END
        FROM outbox_messages
        WHERE "AggregateId" = {literal(order_id)};
    """) == "1"


def order_paid_published(order_id):
    return sql("order_db", f"""
        SELECT count(*)
        FROM outbox_messages
        WHERE "AggregateId" = {literal(order_id)}
          AND "RoutingKey" = 'order.paid'
          AND "PublishedAtUtc" IS NOT NULL;
    """) == "1"


def queue_idle():
    output = run([
        "docker", "compose", "exec", "-T",
        "rabbitmq", "rabbitmqctl",
        "--quiet", "list_queues", "-p", "/",
        "name",
        "messages_ready",
        "messages_unacknowledged",
    ])

    expected = {
        "license-vault.order-paid",
        "order.license-events",
    }

    found = set()

    for line in output.splitlines():
        parts = line.split()

        if len(parts) == 3 and parts[0] in expected:
            found.add(parts[0])

            if parts[1:] != ["0", "0"]:
                return False

    return found == expected


def disable_only_key(product_id):
    licenses = request(
        "GET",
        f"/licenses/products/{product_id}",
        COORDINATOR,
    )

    license_id = licenses[0]["id"]

    request(
        "PATCH",
        f"/licenses/{license_id}/disable",
        COORDINATOR,
    )


def main():
    print("=================================")
    print(" License Allocation E2E Testing")
    print("=================================")
    print(f"Run ID: {RUN_ID}")

    confirmation = input(
        "Type TEST to confirm development environment: "
    )

    if confirmation != "TEST":
        raise SystemExit("Test canceled.")

    global MANAGER, COORDINATOR

    MANAGER = login("catalog_manager")
    COORDINATOR = login("license_coordinator")

    # TC01: Single license
    p1 = create_product("single", 2)
    o1 = make_order(p1, 1)

    check(
        "TC01: Single license allocated",
        wait_for(lambda: fulfillment(o1) == "ASSIGNED")
        and order_status(o1) == "COMPLETED"
        and assigned_count(o1) == 1,
    )

    # TC02: Multiple licenses
    p2 = create_product("multiple", 5)
    o2 = make_order(p2, 5)

    check(
        "TC02: Five licenses allocated",
        wait_for(lambda: fulfillment(o2) == "ASSIGNED")
        and order_status(o2) == "COMPLETED"
        and assigned_count(o2) == 5
        and available_count(p2) == 0,
    )

    # TC03: Duplicate OrderPaid event
    sql("order_db", f"""
        UPDATE outbox_messages
        SET "PublishedAtUtc" = NULL,
            "NextRetryAtUtc" = now()
        WHERE "AggregateId" = {literal(o1)}
          AND "RoutingKey" = 'order.paid';
    """)

    sent = wait_for(
        lambda: order_paid_published(o1)
    )

    check(
        "TC03: Duplicate OrderPaid does not allocate again",
        sent
        and wait_for(queue_idle)
        and assigned_count(o1) == 1
        and available_count(p1) == 1,
    )

    # TC04: Duplicate KeyAssigned event
    sql("license_db", f"""
        UPDATE allocation_results
        SET "PublishedAtUtc" = NULL,
            "NextRetryAtUtc" = now()
        WHERE "OrderId" = {literal(o1)};
    """)

    sent = wait_for(lambda: published(
        "license_db",
        "allocation_results",
        "OrderId",
        o1,
    ))

    result_count = sql("order_db", f"""
        SELECT count(*)
        FROM fulfillment_results
        WHERE "OrderId" = {literal(o1)};
    """)

    check(
        "TC04: Duplicate fulfillment is idempotent",
        sent
        and wait_for(queue_idle)
        and result_count == "1"
        and order_status(o1) == "COMPLETED",
    )

    # TC05 and TC06: Insufficient stock
    p3 = create_product("insufficient", 2)
    o3 = make_order(p3, 3)

    completed = wait_for(
        lambda: fulfillment(o3) == "OUT_OF_STOCK"
    )

    check(
        "TC05: Insufficient stock detected",
        completed and order_status(o3) == "PAID",
    )

    check(
        "TC06: No partial allocation",
        completed
        and assigned_count(o3) == 0
        and available_count(p3) == 2,
    )

    # TC07: Competing orders
    p4 = create_product("competing", 1)

    o4 = make_order(p4, 1)
    o5 = make_order(p4, 1)

    settled = wait_for(
        lambda: bool(fulfillment(o4))
        and bool(fulfillment(o5))
    )

    statuses = sorted([
        fulfillment(o4),
        fulfillment(o5),
    ])

    check(
        "TC07: Only one competing order succeeds",
        settled
        and statuses == ["ASSIGNED", "OUT_OF_STOCK"]
        and assigned_count(o4) + assigned_count(o5) == 1
        and available_count(p4) == 0,
    )

    # TC08: Disabled license
    p5 = create_product("disabled", 1)

    disable_only_key(p5)

    o6 = make_order(p5, 1)

    check(
        "TC08: Disabled license is not assigned",
        wait_for(
            lambda: fulfillment(o6) == "OUT_OF_STOCK"
        )
        and assigned_count(o6) == 0,
    )

    # TC09: Order outbox
    check(
        "TC09: All Order Outbox events published",
        wait_for(lambda: all(
            order_outbox_published(oid)
            for oid in TEST_ORDERS
        )),
    )

    # TC10: Allocation outbox
    check(
        "TC10: All Allocation Outbox events published",
        wait_for(lambda: all(
            published(
                "license_db",
                "allocation_results",
                "OrderId",
                oid,
            )
            for oid in TEST_ORDERS
        )),
    )

    # TC11: Unique order item assignment
    order_ids = ",".join(
        literal(oid) for oid in TEST_ORDERS
    )

    duplicates = sql("license_db", f"""
        SELECT count(*)
        FROM (
            SELECT "OrderItemId"
            FROM license_keys
            WHERE "OrderId" IN ({order_ids})
              AND "OrderItemId" IS NOT NULL
            GROUP BY "OrderItemId"
            HAVING count(*) > 1
        ) duplicates;
    """)

    check(
        "TC11: No duplicate order item assignments",
        duplicates == "0",
    )

    # TC12: Plaintext does not enter event payload
    exposed = sql("license_db", f"""
        SELECT count(*)
        FROM allocation_results
        WHERE "OrderId" IN ({order_ids})
          AND "Payload"::text LIKE
              '%DEMO-ALLOC-{RUN_ID}%';
    """)

    check(
        "TC12: No demo plaintext keys in events",
        exposed == "0",
    )


if __name__ == "__main__":
    try:
        main()

    except Exception as exc:
        FAILED += 1

        print(
            f"[ERROR] {type(exc).__name__}: {exc}"
        )

        if isinstance(exc, HTTPError):
            print("HTTP status:", exc.code)

    finally:
        print("\n=================================")
        print(f"RUN ID : {RUN_ID}")
        print(f"PASSED : {PASSED}")
        print(f"FAILED : {FAILED}")
        print(f"TOTAL  : {PASSED + FAILED}")
        print("=================================")

        print("\nTest product IDs:")

        for product_id in TEST_PRODUCTS:
            print(product_id)

        print("\nTest order IDs:")

        for order_id in TEST_ORDERS:
            print(order_id)

    raise SystemExit(
        0 if PASSED == 12 and FAILED == 0 else 1
    )

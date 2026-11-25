#!/usr/bin/env python3

import importlib.util
import sys
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor

source = Path(__file__).with_name(
    "test-license-allocation.py"
)

spec = importlib.util.spec_from_file_location(
    "allocation_test", source
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

STOCK = 10
ORDERS = 20


def consumer_count():
    output = t.run([
        "docker", "compose", "exec", "-T",
        "rabbitmq", "rabbitmqctl",
        "--quiet", "list_queues", "-p", "/",
        "name", "consumers"
    ])

    for line in output.splitlines():
        parts = line.split()

        if len(parts) == 2 and \
                parts[0] == "license-vault.order-paid":
            return int(parts[1])

    return 0


def main():
    print("=== CONCURRENT ALLOCATION TEST ===")
    print("Run ID:", t.RUN_ID)

    if input("Development only. Type TEST: ") != "TEST":
        raise SystemExit("Canceled.")

    if consumer_count() < 2:
        raise RuntimeError(
            "At least two consumers required."
        )

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")

    product_id = t.create_product(
        "concurrency", STOCK
    )

    print("Creating 20 competing orders...")

    with ThreadPoolExecutor(max_workers=8) as pool:
        order_ids = list(pool.map(
            lambda _: t.make_order(product_id, 1),
            range(ORDERS),
        ))

    ids = ",".join(
        t.literal(oid) for oid in order_ids
    )

    def result_count():
        return int(t.sql("order_db", f"""
            SELECT count(*)
            FROM fulfillment_results
            WHERE "OrderId" IN ({ids});
        """))

    settled = t.wait_for(
        lambda: result_count() == ORDERS,
        timeout=240,
    )

    t.check(
        "CT01: All 20 orders processed",
        settled,
    )

    if not settled:
        raise RuntimeError(
            "Not all orders were processed."
        )

    assigned = int(t.sql("order_db", f"""
        SELECT count(*)
        FROM fulfillment_results
        WHERE "OrderId" IN ({ids})
          AND "Status" = 'ASSIGNED';
    """))

    missing = int(t.sql("order_db", f"""
        SELECT count(*)
        FROM fulfillment_results
        WHERE "OrderId" IN ({ids})
          AND "Status" = 'OUT_OF_STOCK';
    """))

    t.check(
        "CT02: Exactly 10 successful orders",
        assigned == STOCK,
    )

    t.check(
        "CT03: Exactly 10 out-of-stock orders",
        missing == ORDERS - STOCK,
    )

    count = int(t.sql("license_db", f"""
        SELECT count(*)
        FROM license_keys
        WHERE "ProductId" = {t.literal(product_id)}
          AND "OrderId" IN ({ids})
          AND "Status" = 'ASSIGNED';
    """))

    t.check(
        "CT04: Exactly 10 assigned licenses",
        count == STOCK,
    )

    unique_items = int(t.sql("license_db", f"""
        SELECT count(DISTINCT "OrderItemId")
        FROM license_keys
        WHERE "ProductId" = {t.literal(product_id)}
          AND "OrderId" IN ({ids})
          AND "Status" = 'ASSIGNED';
    """))

    t.check(
        "CT05: No duplicate order item assignment",
        unique_items == STOCK,
    )

    t.check(
        "CT06: Available stock is zero",
        t.available_count(product_id) == 0,
    )

    before = t.sql("license_db", f"""
        SELECT "Id", "OrderId", "OrderItemId"
        FROM license_keys
        WHERE "ProductId" = {t.literal(product_id)}
        ORDER BY "Id";
    """)

    print("Replaying all 20 OrderPaid events...")

    t.sql("order_db", f"""
        UPDATE outbox_messages
        SET "PublishedAtUtc" = NULL,
            "NextRetryAtUtc" = now()
        WHERE "AggregateId" IN ({ids})
          AND "RoutingKey" = 'order.paid';
    """)

    sent = t.wait_for(
        lambda: t.sql("order_db", f"""
            SELECT count(*)
            FROM outbox_messages
            WHERE "AggregateId" IN ({ids})
              AND "RoutingKey" = 'order.paid'
              AND "PublishedAtUtc" IS NOT NULL;
        """) == str(ORDERS),
        timeout=240,
    )

    drained = t.wait_for(
        t.queue_idle,
        timeout=120,
    )

    after = t.sql("license_db", f"""
        SELECT "Id", "OrderId", "OrderItemId"
        FROM license_keys
        WHERE "ProductId" = {t.literal(product_id)}
        ORDER BY "Id";
    """)

    t.check(
        "CT07: Replay does not change assignments",
        sent and drained and before == after,
    )

    t.check(
        "CT08: One fulfillment result per order",
        result_count() == ORDERS,
    )


if __name__ == "__main__":
    try:
        main()

    except Exception as exc:
        t.FAILED += 1
        print(f"[ERROR] {type(exc).__name__}: {exc}")

    finally:
        print("\n============================")
        print("RUN ID :", t.RUN_ID)
        print("PASSED :", t.PASSED)
        print("FAILED :", t.FAILED)
        print("TOTAL  :", t.PASSED + t.FAILED)
        print("============================")

        print("Test products:", t.TEST_PRODUCTS)
        print("Test orders:", t.TEST_ORDERS)

    sys.exit(
        0 if t.PASSED == 8
        and t.FAILED == 0 else 1
    )

#!/usr/bin/env python3

import base64
import getpass
import importlib.util
import json
import uuid
import sys

from pathlib import Path

source = Path(__file__).with_name(
    "test-license-invalid-events.py"
)

spec = importlib.util.spec_from_file_location(
    "invalid_events", source
)

mq = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mq)

t = mq.t


def main():
    print("=================================")
    print(" Unpaid Order Protection Testing")
    print("=================================")
    print("Run ID:", t.RUN_ID)

    if input("Development only. Type TEST: ") != "TEST":
        raise SystemExit("Canceled.")

    username = input("RabbitMQ username: ")
    password = getpass.getpass("RabbitMQ password: ")

    mq.AUTH = base64.b64encode(
        f"{username}:{password}".encode()
    ).decode()

    mq.mq("GET", "/overview")

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")

    # Create isolated demo inventory.
    product_id = t.create_product("unpaid", 1)

    order_id = str(uuid.uuid4())
    item_id = str(uuid.uuid4())

    t.TEST_ORDERS.append(order_id)

    # Create a PENDING order.
    # No payment and no OrderPaid event.
    t.sql("order_db", f"""
        BEGIN;

        INSERT INTO orders (
            "Id",
            "KeycloakUserId",
            "TotalAmount",
            "Status",
            "CreatedAtUtc"
        )
        VALUES (
            {t.literal(order_id)},
            {t.literal(t.CUSTOMER_ID)},
            100000,
            'PENDING',
            now()
        );

        INSERT INTO order_items (
            "Id",
            "OrderId",
            "ProductId",
            "Price"
        )
        VALUES (
            {t.literal(item_id)},
            {t.literal(order_id)},
            {t.literal(product_id)},
            100000
        );

        COMMIT;
    """)

    t.check(
        "UT01: Test order is PENDING",
        t.order_status(order_id) == "PENDING"
    )

    # Simulate an invalid KeyAssigned event.
    payload = {
        "orderId": order_id,
        "keycloakUserId": t.CUSTOMER_ID,
        "customerEmail": None,
        "keys": [
            {
                "itemId": item_id,
                "productId": product_id,
                "licenseId": str(uuid.uuid4())
            }
        ]
    }

    rejected = mq.rejected(
        mq.ORDER_DLQ,
        "key.assigned",
        json.dumps(payload)
    )

    t.check(
        "UT02: Unpaid fulfillment rejected to DLQ",
        rejected
    )

    t.check(
        "UT03: Order remains PENDING",
        t.order_status(order_id) == "PENDING"
    )

    t.check(
        "UT04: No fulfillment result created",
        t.fulfillment(order_id) == ""
    )

    t.check(
        "UT05: No license assigned",
        t.assigned_count(order_id) == 0
        and t.available_count(product_id) == 1
    )

    print("\nTest order:", order_id)
    print("Test product:", product_id)


if __name__ == "__main__":
    try:
        main()

    except Exception as exc:
        t.FAILED += 1
        print(f"[ERROR] {type(exc).__name__}: {exc}")

    finally:
        print("\n=================================")
        print("PASSED :", t.PASSED)
        print("FAILED :", t.FAILED)
        print("TOTAL  :", t.PASSED + t.FAILED)
        print("=================================")

    sys.exit(
        0 if t.PASSED == 5
        and t.FAILED == 0 else 1
    )

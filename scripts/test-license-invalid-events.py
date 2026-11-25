#!/usr/bin/env python3

import base64
import getpass
import importlib.util
import json
import sys
import uuid

from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import quote

source = Path(__file__).with_name(
    "test-license-allocation.py"
)

spec = importlib.util.spec_from_file_location(
    "allocation_test", source
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

MANAGEMENT = "http://127.0.0.1:15672/api"

EXCHANGE = "marketplace.events"

LICENSE_DLQ = "license-vault.order-paid.dead"
ORDER_DLQ = "order.license-events.dead"

AUTH = None


def mq(method, path, data=None):
    headers = {
        "Authorization": "Basic " + AUTH,
        "Content-Type": "application/json",
    }

    body = (
        json.dumps(data).encode()
        if data is not None else None
    )

    req = Request(
        MANAGEMENT + path,
        headers=headers,
        data=body,
        method=method,
    )

    with urlopen(req, timeout=30) as response:
        return json.load(response)


def publish(route, payload):
    result = mq(
        "POST",
        "/exchanges/%2F/"
        + quote(EXCHANGE, safe="")
        + "/publish",
        {
            "properties": {
                "delivery_mode": 2,
                "content_type": "application/json",
                "message_id": str(uuid.uuid4()),
            },
            "routing_key": route,
            "payload": payload,
            "payload_encoding": "string",
        }
    )

    if not result.get("routed"):
        raise RuntimeError(
            "Event was not routed."
        )


def dead_count(queue):
    result = mq(
        "GET",
        "/queues/%2F/" + quote(queue, safe="")
    )

    return result["messages"]


def rejected(queue, route, payload):
    before = dead_count(queue)

    publish(route, payload)

    return t.wait_for(
        lambda: dead_count(queue) == before + 1,
        timeout=60,
    )


def main():
    global AUTH

    print("=== INVALID EVENT TEST ===")
    print("Run ID:", t.RUN_ID)

    if input("Development only. Type TEST: ") != "TEST":
        raise SystemExit("Canceled.")

    username = input("RabbitMQ username: ")
    password = getpass.getpass("RabbitMQ password: ")

    AUTH = base64.b64encode(
        f"{username}:{password}".encode()
    ).decode()

    mq("GET", "/overview")

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")

    product_id = t.create_product(
        "invalid-events", 2
    )

    order_id = t.make_order(product_id, 1)

    if not t.wait_for(
        lambda: t.fulfillment(order_id) == "ASSIGNED",
        timeout=120,
    ):
        raise RuntimeError(
            "Could not prepare valid test order."
        )

    order_literal = t.literal(order_id)

    order_event = json.loads(
        t.sql("order_db", f"""
            SELECT "Payload"::text
            FROM outbox_messages
            WHERE "AggregateId" = {order_literal}
              AND "RoutingKey" = 'order.paid';
        """)
    )

    assigned_event = json.loads(
        t.sql("license_db", f"""
            SELECT "Payload"::text
            FROM allocation_results
            WHERE "OrderId" = {order_literal};
        """)
    )

    # FT07: Malformed JSON
    t.check(
        "IT01: Malformed JSON sent to License DLQ",
        rejected(
            LICENSE_DLQ,
            "order.paid",
            '{"invalid":',
        ),
    )

    # FT08: Conflicting event for existing order.
    conflicting = dict(order_event)
    conflicting["amount"] += 100000

    t.check(
        "IT02: Conflicting OrderPaid rejected",
        rejected(
            LICENSE_DLQ,
            "order.paid",
            json.dumps(conflicting),
        ),
    )

    # FT09: Wrong customer.
    wrong_customer = dict(assigned_event)
    wrong_customer["keycloakUserId"] = (
        f"wrong-customer-{t.RUN_ID}"
    )

    t.check(
        "IT03: Wrong customer rejected",
        rejected(
            ORDER_DLQ,
            "key.assigned",
            json.dumps(wrong_customer),
        ),
    )

    # Conflicting fulfillment result.
    conflict_result = json.dumps({
        "orderId": order_id,
        "productId": product_id,
        "reason": "DEMO conflicting result",
    })

    t.check(
        "IT04: Conflicting fulfillment rejected",
        rejected(
            ORDER_DLQ,
            "key.out_of_stock",
            conflict_result,
        ),
    )

    # Unknown order.
    unknown_order = dict(assigned_event)
    unknown_order["orderId"] = str(uuid.uuid4())

    t.check(
        "IT05: Unknown order rejected",
        rejected(
            ORDER_DLQ,
            "key.assigned",
            json.dumps(unknown_order),
        ),
    )

    # Ensure the original assignment remains unchanged.
    t.check(
        "IT06: Valid order remains COMPLETED",
        t.order_status(order_id) == "COMPLETED"
        and t.fulfillment(order_id) == "ASSIGNED"
        and t.assigned_count(order_id) == 1
        and t.available_count(product_id) == 1,
    )

    print("\nLicense DLQ:", dead_count(LICENSE_DLQ))
    print("Order DLQ:", dead_count(ORDER_DLQ))


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

    sys.exit(
        0 if t.PASSED == 6
        and t.FAILED == 0 else 1
    )

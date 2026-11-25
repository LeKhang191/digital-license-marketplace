#!/usr/bin/env python3

import importlib.util
import sys
import time
from pathlib import Path

source = Path(__file__).with_name(
    "test-license-allocation.py"
)

spec = importlib.util.spec_from_file_location(
    "allocation_test", source
)

t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)


def compose(*args):
    return t.run([
        "docker", "compose", *args
    ])


def ensure(condition, message):
    if not condition:
        raise RuntimeError(message)


def broker_ready():
    try:
        compose(
            "exec", "-T", "rabbitmq",
            "rabbitmq-diagnostics",
            "-q", "check_running"
        )
        return True
    except Exception:
        return False


def broker_node():
    return compose(
        "exec", "-T", "rabbitmq",
        "rabbitmqctl", "-q", "eval",
        "node()."
    )


def retry_count(order_id):
    value = t.sql("order_db", f"""
        SELECT "RetryCount"
        FROM outbox_messages
        WHERE "AggregateId" = {t.literal(order_id)}
          AND "RoutingKey" = 'order.paid';
    """)

    return int(value or 0)


def order_event_published(order_id):
    return t.published(
        "order_db",
        "outbox_messages",
        "AggregateId",
        order_id,
    )


def completed(order_id):
    return (
        t.fulfillment(order_id) == "ASSIGNED"
        and t.order_status(order_id) == "COMPLETED"
        and t.assigned_count(order_id) == 1
    )


def queue_counts(queue):
    output = compose(
        "exec", "-T", "rabbitmq",
        "rabbitmqctl", "--quiet",
        "list_queues", "-p", "/",
        "name",
        "messages_ready",
        "messages_unacknowledged",
        "consumers"
    )

    for line in output.splitlines():
        parts = line.split()

        if len(parts) == 4 and parts[0] == queue:
            return tuple(map(int, parts[1:]))

    raise RuntimeError(
        f"Queue not found: {queue}"
    )


def consumers_ready():
    try:
        return (
            queue_counts(
                "license-vault.order-paid"
            )[2] == 1
            and queue_counts(
                "order.license-events"
            )[2] == 1
        )
    except Exception:
        return False


def main():
    print("================================")
    print(" License Resilience Testing")
    print("================================")
    print("Run ID:", t.RUN_ID)

    confirm = input(
        "Development environment only. Type TEST: "
    )

    if confirm != "TEST":
        raise SystemExit("Canceled.")

    ensure(
        consumers_ready(),
        "Both consumers must be running."
    )

    ensure(
        t.wait_for(t.queue_idle, timeout=30),
        "Business queues are not empty."
    )

    for queue in [
        "license-vault.order-paid.dead",
        "order.license-events.dead"
    ]:
        ensure(
            queue_counts(queue)[:2] == (0, 0),
            f"Dead letter queue not empty: {queue}"
        )

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")

    # Create isolated demo products before stopping RabbitMQ.
    p1 = t.create_product("offline", 2)
    p2 = t.create_product("persistent", 2)

    # FT01 - FT03: RabbitMQ offline and recovery.
    print("\nStopping RabbitMQ...", flush=True)

    try:
        compose("stop", "rabbitmq")

        o1 = t.make_order(p1, 1)

        retried = t.wait_for(
            lambda: retry_count(o1) > 0,
            timeout=120,
        )

        t.check(
            "FT01: Offline event retained in Order Outbox",
            not order_event_published(o1)
            and t.order_status(o1) == "PAID"
            and t.assigned_count(o1) == 0,
        )

        t.check(
            "FT02: Outbox records a failed publish and retry",
            retried
        )

    finally:
        print("\nRestoring RabbitMQ...", flush=True)
        compose("start", "rabbitmq")

    ensure(
        t.wait_for(broker_ready, timeout=120),
        "RabbitMQ did not recover."
    )

    t.check(
        "FT03: Order completed automatically after recovery",
        t.wait_for(
            lambda: completed(o1)
            and order_event_published(o1),
            timeout=180,
        ),
    )

    ensure(
        t.wait_for(consumers_ready, timeout=90),
        "Consumers did not reconnect."
    )

    ensure(
        t.wait_for(t.queue_idle, timeout=30),
        "Business queues are not empty."
    )

    # FT04: Persistent message survives broker recreation.
    print(
        "\nStopping License Vault Consumer...",
        flush=True,
    )

    try:
        compose("stop", "license-vault")

        ensure(
            t.wait_for(
                lambda: queue_counts(
                    "license-vault.order-paid"
                )[2] == 0,
                timeout=30,
            ),
            "License Consumer did not stop."
        )

        o2 = t.make_order(p2, 1)

        ensure(
            t.wait_for(
                lambda: order_event_published(o2)
                and queue_counts(
                    "license-vault.order-paid"
                )[:2] == (1, 0),
                timeout=120,
            ),
            "Persistent event was not queued."
        )

        node_before = broker_node()

        # Stop publisher so the test can verify that
        # the original message survives without republishing.
        compose("stop", "order")

        print(
            "Recreating RabbitMQ with existing volume...",
            flush=True,
        )

        compose(
            "up", "-d", "--no-deps",
            "--force-recreate", "rabbitmq"
        )

        ensure(
            t.wait_for(broker_ready, timeout=120),
            "RabbitMQ failed after recreate."
        )

        survived = (
            broker_node() == node_before
            and queue_counts(
                "license-vault.order-paid"
            )[:2] == (1, 0)
        )

        t.check(
            "FT04: Persistent message survives broker recreate",
            survived,
        )

        ensure(
            survived,
            "Persistent message or broker node was lost."
        )

    finally:
        compose("start", "rabbitmq")

        ensure(
            t.wait_for(broker_ready, timeout=120),
            "RabbitMQ requires manual recovery."
        )

        compose("start", "order", "license-vault")

    t.check(
        "FT04B: Retained event consumed after restart",
        t.wait_for(
            lambda: completed(o2),
            timeout=180,
        )
        and t.assigned_count(o2) == 1,
    )

    t.check(
        "FT04C: Both consumers reconnected",
        t.wait_for(consumers_ready, timeout=90),
    )

    t.check(
        "FT04D: Business queues drained successfully",
        t.wait_for(t.queue_idle, timeout=60),
    )


if __name__ == "__main__":
    try:
        main()

    except Exception as exc:
        t.FAILED += 1

        print(
            f"[ERROR] {type(exc).__name__}: {exc}",
            flush=True,
        )

    finally:
        print("\n================================")
        print("RUN ID :", t.RUN_ID)
        print("PASSED :", t.PASSED)
        print("FAILED :", t.FAILED)
        print("TOTAL  :", t.PASSED + t.FAILED)
        print("================================")

        print("\nTest product IDs:")
        for product_id in t.TEST_PRODUCTS:
            print(product_id)

        print("\nTest order IDs:")
        for order_id in t.TEST_ORDERS:
            print(order_id)

    sys.exit(
        0 if t.PASSED == 7
        and t.FAILED == 0 else 1
    )

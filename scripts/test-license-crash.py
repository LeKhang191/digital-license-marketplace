#!/usr/bin/env python3

import importlib.util
import os
import sys
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


def allocation_count(order_id):
    return int(t.sql("license_db", f"""
        SELECT count(*)
        FROM allocation_results
        WHERE "OrderId" = {t.literal(order_id)};
    """))


def assignment_snapshot(order_id):
    return t.sql("license_db", f"""
        SELECT "Id", "OrderItemId", "OrderId"
        FROM license_keys
        WHERE "OrderId" = {t.literal(order_id)}
        ORDER BY "Id";
    """)


def container_running(container_id):
    return t.run([
        "docker", "inspect",
        "--format", "{{.State.Running}}",
        container_id
    ]) == "true"


def queue_state():
    result = compose(
        "exec", "-T", "rabbitmq",
        "rabbitmqctl", "--quiet",
        "list_queues", "-p", "/",
        "name",
        "messages_ready",
        "messages_unacknowledged",
        "consumers",
    )

    for line in result.splitlines():
        parts = line.split()

        if len(parts) == 4 and \
                parts[0] == "license-vault.order-paid":
            return tuple(map(int, parts[1:]))

    raise RuntimeError("OrderPaid queue not found.")


def main():
    print("=== CRASH AND REDELIVERY TEST ===")

    if input("Development only. Type TEST: ") != "TEST":
        raise SystemExit("Canceled.")

    t.CUSTOMER_ID = os.environ[
        "CRASH_TEST_CUSTOMER"
    ]

    container_id = compose(
        "ps", "-q", "license-vault"
    )

    ensure(
        bool(container_id)
        and len(container_id.splitlines()) == 1,
        "Exactly one License Vault instance required."
    )

    ensure(
        queue_state() == (0, 0, 1),
        "Expected an empty queue with one consumer."
    )

    restart_policy = t.run([
        "docker", "inspect",
        "--format",
        "{{.HostConfig.RestartPolicy.Name}}",
        container_id
    ])

    ensure(
        restart_policy == "no",
        "Automatic container restart must be disabled."
    )

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")

    product_id = t.create_product("crash", 2)

    order_id = t.make_order(product_id, 1)

    crashed = t.wait_for(
        lambda: not container_running(container_id),
        timeout=120
    )

    logs = compose(
        "logs", "--no-color", "license-vault"
    )

    crash_observed = (
        crashed
        and "FT06: Crash after commit before ACK" in logs
    )

    t.check(
        "CR01: Controlled consumer crash observed",
        crash_observed
    )

    ensure(
        crash_observed,
        "Controlled crash was not observed."
    )

    t.check(
        "CR02: License assignment committed before crash",
        t.assigned_count(order_id) == 1
    )

    t.check(
        "CR03: Allocation result committed before crash",
        allocation_count(order_id) == 1
    )

    t.check(
        "CR04: Unacked event returned to RabbitMQ",
        t.wait_for(
            lambda: queue_state() == (1, 0, 0),
            timeout=60
        )
    )

    before = assignment_snapshot(order_id)

    print("Restarting License Vault...", flush=True)

    compose("start", "license-vault")

    def redelivery_observed():
        output = compose(
            "logs", "--no-color", "license-vault"
        )

        return (
            "FT06: Redelivery processed successfully"
            in output
            and order_id in output
        )

    t.check(
        "CR05: RabbitMQ redelivery observed",
        t.wait_for(
            redelivery_observed,
            timeout=120
        )
    )

    completed = t.wait_for(
        lambda:
            t.order_status(order_id) == "COMPLETED"
            and t.fulfillment(order_id) == "ASSIGNED",
        timeout=120
    )

    t.check(
        "CR06: Order completed after recovery",
        completed
    )

    after = assignment_snapshot(order_id)

    t.check(
        "CR07: No duplicate license assignment",
        bool(before)
        and before == after
        and t.assigned_count(order_id) == 1
        and t.available_count(product_id) == 1
        and allocation_count(order_id) == 1
    )

    t.check(
        "CR08: Business queue drained",
        t.wait_for(
            lambda: queue_state() == (0, 0, 1),
            timeout=60
        )
    )

    print("Test order:", order_id)
    print("Test product:", product_id)


if __name__ == "__main__":
    try:
        main()

    except Exception as exc:
        t.FAILED += 1
        print(f"[ERROR] {type(exc).__name__}: {exc}")

    finally:
        print("============================")
        print("PASSED :", t.PASSED)
        print("FAILED :", t.FAILED)
        print("TOTAL  :", t.PASSED + t.FAILED)
        print("============================")

        print("Restore original Consumer source now.")

    sys.exit(
        0 if t.PASSED == 8
        and t.FAILED == 0 else 1
    )

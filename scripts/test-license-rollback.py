#!/usr/bin/env python3

import importlib.util
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

SUFFIX = t.RUN_ID
TRIGGER = f"test_rollback_{SUFFIX}"
FUNCTION = f"test_fail_commit_{SUFFIX}"
SEQUENCE = f"test_rollback_attempt_{SUFFIX}"


def cleanup():
    t.sql("license_db", f"""
        DROP TRIGGER IF EXISTS {TRIGGER}
            ON allocation_results;

        DROP FUNCTION IF EXISTS {FUNCTION}();

        DROP SEQUENCE IF EXISTS {SEQUENCE};
    """)


def allocation_count(order_id):
    return int(t.sql("license_db", f"""
        SELECT count(*)
        FROM allocation_results
        WHERE "OrderId" = {t.literal(order_id)};
    """))


def attempts():
    value = t.sql("license_db", f"""
        SELECT CASE
            WHEN is_called THEN last_value
            ELSE 0
        END
        FROM {SEQUENCE};
    """)

    return int(value)


def main():
    print("=== TRANSACTION ROLLBACK TEST ===")
    print("Run ID:", t.RUN_ID)

    if input("Development only. Type TEST: ") != "TEST":
        raise SystemExit("Canceled.")

    t.MANAGER = t.login("catalog_manager")
    t.COORDINATOR = t.login("license_coordinator")

    product_id = t.create_product("rollback", 2)

    t.check(
        "RT01: Two available demo licenses",
        t.available_count(product_id) == 2
    )

    order_id = None

    try:
        t.sql("license_db", f"""
            BEGIN;

            CREATE SEQUENCE {SEQUENCE};

            CREATE FUNCTION {FUNCTION}()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW."CustomerId" =
                    {t.literal(t.CUSTOMER_ID)}
                THEN
                    PERFORM nextval(
                        '{SEQUENCE}'
                    );

                    RAISE EXCEPTION
                        'Controlled test commit failure';
                END IF;

                RETURN NEW;
            END;
            $$;

            CREATE CONSTRAINT TRIGGER {TRIGGER}
            AFTER INSERT ON allocation_results
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW
            EXECUTE FUNCTION {FUNCTION}();

            COMMIT;
        """)

        print("Commit failure trigger installed.")

        order_id = t.make_order(product_id, 2)

        failed_at_commit = t.wait_for(
            lambda: attempts() > 0,
            timeout=90
        )

        t.check(
            "RT02: Commit failure was triggered",
            failed_at_commit
        )

        if not failed_at_commit:
            raise RuntimeError(
                "Commit failure was not observed."
            )

        t.check(
            "RT03: No licenses committed",
            t.assigned_count(order_id) == 0
        )

        t.check(
            "RT04: Inventory unchanged",
            t.available_count(product_id) == 2
        )

        t.check(
            "RT05: Allocation result rolled back",
            allocation_count(order_id) == 0
        )

        t.check(
            "RT06: Order not completed after failure",
            t.order_status(order_id) == "PAID"
            and t.fulfillment(order_id) == ""
        )

    finally:
        print("Removing test failure trigger...")
        cleanup()
        print("Test database objects removed.")

    recovered = t.wait_for(
        lambda:
            t.order_status(order_id) == "COMPLETED"
            and t.fulfillment(order_id) == "ASSIGNED",
        timeout=120
    )

    t.check(
        "RT07: Consumer retry completes the order",
        recovered
    )

    t.check(
        "RT08: Exactly two licenses assigned",
        t.assigned_count(order_id) == 2
        and t.available_count(product_id) == 0
    )

    t.check(
        "RT09: Exactly one allocation result",
        allocation_count(order_id) == 1
    )

    t.check(
        "RT10: Allocation outbox published",
        t.wait_for(
            lambda: t.published(
                "license_db",
                "allocation_results",
                "OrderId",
                order_id
            ),
            timeout=90
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
        print("\n============================")
        print("RUN ID :", t.RUN_ID)
        print("PASSED :", t.PASSED)
        print("FAILED :", t.FAILED)
        print("TOTAL  :", t.PASSED + t.FAILED)
        print("============================")

    sys.exit(
        0 if t.PASSED == 10
        and t.FAILED == 0 else 1
    )

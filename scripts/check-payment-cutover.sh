#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

echo "===== ORDER DATABASE ====="

docker compose exec -T postgres \
    psql -U app -d order_db -v ON_ERROR_STOP=1 <<'SQL'

SELECT
    "Status",
    count(*) AS total
FROM orders
GROUP BY "Status"
ORDER BY "Status";

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM orders
        WHERE "PaymentRef" IS NULL
           OR trim("PaymentRef") = ''
           OR "TotalAmount" <= 0
    ) THEN
        RAISE EXCEPTION 'Invalid payment data';
    END IF;

    IF EXISTS (
        SELECT "PaymentRef"
        FROM orders
        GROUP BY "PaymentRef"
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Duplicate PaymentRef';
    END IF;

    IF EXISTS (
        SELECT "VnpTransactionNo"
        FROM orders
        WHERE "VnpTransactionNo" IS NOT NULL
          AND trim("VnpTransactionNo") <> ''
        GROUP BY "VnpTransactionNo"
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Duplicate VNPay TransactionNo';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM orders
        WHERE "Status" IN ('PAID', 'COMPLETED')
          AND (
              "PaidAtUtc" IS NULL
              OR "VnpTransactionNo" IS NULL
              OR trim("VnpTransactionNo") = ''
              OR "VnpPayDate" IS NULL
              OR trim("VnpPayDate") = ''
          )
    ) THEN
        RAISE EXCEPTION 'Paid orders missing transaction metadata';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM orders
        WHERE "Status" NOT IN (
            'PENDING',
            'PAID',
            'COMPLETED'
        )
    ) THEN
        RAISE EXCEPTION 'Unexpected order status; manual review required';
    END IF;

    RAISE NOTICE 'Order payment data validation passed';
END $$;

SQL

echo "===== PAYMENT DATABASE ====="

docker compose exec -T postgres \
    psql -U app -d payment_db -v ON_ERROR_STOP=1 <<'SQL'

SELECT count(*) AS existing_payments
FROM payments;

SELECT count(*) AS pending_events
FROM outbox_messages
WHERE "PublishedAtUtc" IS NULL;

SQL

echo "===== RABBITMQ ====="

docker compose exec -T rabbitmq \
    rabbitmqctl list_queues \
    name type consumers messages_ready messages_unacknowledged

echo "[DONE] Preflight inspection completed"

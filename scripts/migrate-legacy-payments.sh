#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

umask 077

: "${LEGACY_PAYMENT_EXPIRE_MINUTES:?Set legacy payment expiry}"

if [[ ! "$LEGACY_PAYMENT_EXPIRE_MINUTES" =~ ^[1-9][0-9]{0,3}$ ]]; then
    echo "[STOP] Invalid expiry"
    exit 1
fi

if (( LEGACY_PAYMENT_EXPIRE_MINUTES > 1440 )); then
    echo "[STOP] Invalid expiry range"
    exit 1
fi

RUNNING="$(docker compose ps --status running --services)"

for service in gateway order payment; do
    if grep -Fxq "$service" <<< "$RUNNING"; then
        echo "[STOP] Stop $service before migration"
        exit 1
    fi
done

bash scripts/backup-payment-cutover.sh
bash scripts/check-payment-cutover.sh

WORK_DIR="$(mktemp -d /tmp/dlm-payment-migration.XXXXXXXX)"

trap 'rm -rf "$WORK_DIR"' EXIT

CSV="$WORK_DIR/payments.csv"

echo "[EXPORT] order_db"

docker compose exec -T postgres \
    psql -X -q -U app -d order_db \
    -v ON_ERROR_STOP=1 -c "
COPY (
    SELECT
        gen_random_uuid(),
        \"Id\",
        \"KeycloakUserId\",
        \"TotalAmount\",
        \"PaymentRef\",
        CASE
            WHEN \"Status\" = 'PENDING'
                THEN 'PENDING'
            WHEN \"Status\" IN ('PAID', 'COMPLETED')
                THEN 'SUCCEEDED'
        END,
        \"VnpTransactionNo\",
        \"VnpPayDate\",
        \"CreatedAtUtc\",
        \"CreatedAtUtc\" +
            make_interval(
                mins => $LEGACY_PAYMENT_EXPIRE_MINUTES
            ),
        \"PaidAtUtc\"
    FROM orders
    ORDER BY \"CreatedAtUtc\", \"Id\"
) TO STDOUT WITH (FORMAT csv);
" > "$CSV"

test -s "$CSV"

echo "[IMPORT] payment_db"

{
    cat <<'SQL'
BEGIN;

LOCK TABLE payments, refunds, outbox_messages
IN ACCESS EXCLUSIVE MODE;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM payments) THEN
        RAISE EXCEPTION 'Payment database is not empty';
    END IF;

    IF EXISTS (SELECT 1 FROM refunds) THEN
        RAISE EXCEPTION 'Refund database is not empty';
    END IF;

    IF EXISTS (SELECT 1 FROM outbox_messages) THEN
        RAISE EXCEPTION 'Payment outbox is not empty';
    END IF;
END $$;

CREATE TEMP TABLE payment_import
(LIKE payments INCLUDING DEFAULTS INCLUDING CONSTRAINTS);

COPY payment_import (
    "Id",
    "OrderId",
    "CustomerId",
    "Amount",
    "PaymentRef",
    "Status",
    "VnpTransactionNo",
    "VnpPayDate",
    "CreatedAtUtc",
    "ExpiresAtUtc",
    "PaidAtUtc"
) FROM STDIN WITH (FORMAT csv);
SQL

    cat "$CSV"
    printf '\\.\n'

    cat <<'SQL'

INSERT INTO payments (
    "Id",
    "OrderId",
    "CustomerId",
    "Amount",
    "PaymentRef",
    "Status",
    "VnpTransactionNo",
    "VnpPayDate",
    "CreatedAtUtc",
    "ExpiresAtUtc",
    "PaidAtUtc"
)
SELECT
    "Id",
    "OrderId",
    "CustomerId",
    "Amount",
    "PaymentRef",
    "Status",
    "VnpTransactionNo",
    "VnpPayDate",
    "CreatedAtUtc",
    "ExpiresAtUtc",
    "PaidAtUtc"
FROM payment_import;

DO $$
BEGIN
    IF (SELECT count(*) FROM payments)
       <> (SELECT count(*) FROM payment_import) THEN
        RAISE EXCEPTION 'Migration count mismatch';
    END IF;

    IF EXISTS (SELECT 1 FROM outbox_messages) THEN
        RAISE EXCEPTION 'Unexpected historical payment events';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM payments
        WHERE "Status" = 'SUCCEEDED'
          AND (
              "PaidAtUtc" IS NULL
              OR "VnpTransactionNo" IS NULL
              OR "VnpPayDate" IS NULL
          )
    ) THEN
        RAISE EXCEPTION 'Incomplete successful payment';
    END IF;
END $$;

SELECT "Status", count(*)
FROM payments
GROUP BY "Status"
ORDER BY "Status";

COMMIT;
SQL

} | docker compose exec -T postgres \
    psql -X -U app -d payment_db \
    -v ON_ERROR_STOP=1

echo "[PASS] Legacy payment migration"
echo "[PASS] Historical events not republished"

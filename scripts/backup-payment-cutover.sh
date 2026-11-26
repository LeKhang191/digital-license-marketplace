#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

umask 077

BACKUP_DIR="$(mktemp -d /tmp/dlm-payment-cutover.XXXXXXXX)"

for db in order_db payment_db; do
    echo "[BACKUP] $db"

    docker compose exec -T postgres \
        pg_dump -U app -Fc "$db" \
        > "$BACKUP_DIR/$db.dump"

    test -s "$BACKUP_DIR/$db.dump"

    docker compose exec -T postgres \
        pg_restore --list \
        < "$BACKUP_DIR/$db.dump" > /dev/null

    echo "[PASS] $db"
done

echo "[DONE] Backup location: $BACKUP_DIR"

ls -lh "$BACKUP_DIR"

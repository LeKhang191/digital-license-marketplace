
#!/usr/bin/env bash

set -Eeuo pipefail
cd "$(dirname "$0")/.."

CATALOG_MIGRATION="20261123061604_InitialCreate"
ORDER_MIGRATION="20261123061608_InitialCreate"
LICENSE_MIGRATION=$(basename "$(find \
    services/license-vault-service/Infrastructure/Persistence/Migrations \
    -maxdepth 1 \
    -name '*_InitialCreate.cs' \
    -print -quit)" .cs)

if [[ -z "$LICENSE_MIGRATION" ]]; then
    echo "ERROR: License initial migration is missing."
    exit 1
fi

PAYMENT_MIGRATION=$(basename "$(find \
    services/payment-service/Infrastructure/Persistence/Migrations \
    -maxdepth 1 \
    -name '*_InitialCreate.cs' \
    -print -quit)" .cs)

if [[ -z "$PAYMENT_MIGRATION" ]]; then
    echo "ERROR: Payment initial migration is missing."
    exit 1
fi

query_db() {
    local database="$1"
    local sql="$2"

    docker compose exec -T postgres \
        psql -X -U app -d "$database" \
        -v ON_ERROR_STOP=1 -Atc "$sql"
}

check_database() {
    local database="$1"
    local migration="$2"

    local table_count
    local history_exists
    local initial_applied

    echo "[Check] Inspecting $database..."

    table_count=$(query_db "$database" "
        SELECT count(*)
        FROM pg_catalog.pg_tables
        WHERE schemaname = 'public';
    ")

    history_exists=$(query_db "$database" "
        SELECT CASE
            WHEN to_regclass('public.\"__EFMigrationsHistory\"')
                 IS NULL THEN 0
            ELSE 1
        END;
    ")

    if [[ "$table_count" == "0" ]]; then
        echo "[OK] $database is empty."
        return 0
    fi

    if [[ "$history_exists" != "1" ]]; then
        echo "[ERROR] $database has existing tables."
        echo "[ERROR] Migration history is missing."
        echo "Review docs/DATABASE_MIGRATIONS.md."
        return 1
    fi

    initial_applied=$(query_db "$database" "
        SELECT count(*)
        FROM public.\"__EFMigrationsHistory\"
        WHERE \"MigrationId\" = '$migration';
    ")

    if [[ "$initial_applied" != "1" ]]; then
        echo "[ERROR] Initial migration is not recorded."
        echo "Database: $database"
        echo "Expected migration: $migration"
        return 1
    fi

    echo "[OK] Initial migration is recorded."
}

echo "================================="
echo " Database Migration"
echo "================================="

docker compose config --quiet

echo "[1/4] Starting PostgreSQL..."
docker compose up -d --wait postgres
if [[ "$(query_db postgres \
    "SELECT count(*) FROM pg_database WHERE datname = 'license_db';")" == "0" ]]; then

    echo "[Database] Creating license_db..."

    docker compose exec -T postgres \
        psql -X -U app -d postgres \
        -v ON_ERROR_STOP=1 \
        -c 'CREATE DATABASE license_db;'
fi
if [[ "$(query_db postgres \
    "SELECT count(*) FROM pg_database WHERE datname = 'payment_db';")" == "0" ]]; then

    echo "[Database] Creating payment_db..."

    docker compose exec -T postgres \
        psql -X -U app -d postgres \
        -v ON_ERROR_STOP=1 \
        -c 'CREATE DATABASE payment_db;'
fi

echo "[2/4] Checking databases..."
check_database catalog_db "$CATALOG_MIGRATION"
check_database order_db "$ORDER_MIGRATION"
check_database license_db "$LICENSE_MIGRATION"
check_database payment_db "$PAYMENT_MIGRATION"

echo "[3/4] Building migration image..."
docker compose --profile migration build migrator

echo "[4/4] Applying migrations..."

docker compose --profile migration run --rm migrator catalog
# Prepare Support before applying destructive Order cleanup.

bash scripts/migrate-support.sh

SUPPORT_CLEANUP="20261127010000_RemoveLegacyCustomerSupport"

cleanup_applied=$(query_db order_db "
    SELECT CASE WHEN to_regclass(
        'public.\"__EFMigrationsHistory\"'
    ) IS NULL THEN 0 ELSE 1 END;
")

if [[ "$cleanup_applied" == "1" ]]; then
    cleanup_applied=$(query_db order_db "
        SELECT count(*)
        FROM \"__EFMigrationsHistory\"
        WHERE \"MigrationId\" = '$SUPPORT_CLEANUP';
    ")
fi

if [[ "$cleanup_applied" == "0" ]]; then

    for service in gateway order support license-vault; do
        if [[ -n "$(docker compose ps --status running -q "$service")" ]]; then
            echo "[STOP] $service must be stopped before Support migration."
            exit 1
        fi
    done

    # Apply all non-destructive Order migrations first.
    docker compose --profile migration run --rm migrator \
        order 20261126120000_AddVerifiedCustomerEmail

    # Preserve legacy data before dropping tables.
    umask 077
    mkdir -p backups

    backup="backups/support-cleanup-$(date +%Y%m%d-%H%M%S).dump"

    docker compose exec -T postgres \
        pg_dump -U app -d order_db -Fc \
        -t support_tickets -t support_messages \
        > "$backup"

    test -s "$backup"

    echo "[PASS] Legacy Support backup created."

    bash scripts/cutover-support-data.sh
fi

docker compose --profile migration run --rm migrator \
    order "$SUPPORT_CLEANUP"
docker compose --profile migration run --rm migrator order
docker compose --profile migration run --rm migrator license
docker compose --profile migration run --rm migrator payment

bash scripts/migrate-notification.sh

echo "================================="
echo " Database migrations completed."
echo "================================="

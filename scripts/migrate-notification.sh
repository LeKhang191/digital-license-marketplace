#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

exists=$(docker compose exec -T postgres \
    psql -X -U app -d postgres \
    -v ON_ERROR_STOP=1 -Atc \
    "SELECT count(*) FROM pg_database
     WHERE datname = 'notification_db';")

if [[ "$exists" == "0" ]]; then
    docker compose exec -T postgres \
        psql -X -U app -d postgres \
        -v ON_ERROR_STOP=1 \
        -c 'CREATE DATABASE notification_db OWNER app;'
fi

docker compose exec -T postgres \
    psql -X -U app -d notification_db \
    -v ON_ERROR_STOP=1 \
    < infra/postgres/notification.sql

echo "[PASS] Notification database ready"

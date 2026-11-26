#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

for service in gateway order support license-vault; do
    if [[ -n "$(docker compose ps --status running -q "$service")" ]]; then
        echo "[STOP] $service must be stopped."
        exit 1
    fi
done

umask 077
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

db() {
    local database="$1"
    shift

    docker compose exec -T postgres \
        psql -X -U app -d "$database" \
        -v ON_ERROR_STOP=1 "$@"
}

legacy=$(db order_db -Atc "
    SELECT count(*)
    FROM pg_tables
    WHERE schemaname = 'public'
      AND tablename IN ('support_tickets', 'support_messages');
")

if [[ "$legacy" == "0" ]]; then
    echo "[PASS] Legacy Support tables already removed."
    exit 0
fi

if [[ "$legacy" != "2" ]]; then
    echo "[STOP] Incomplete legacy Support schema."
    exit 1
fi

tickets=$(db support_db -Atc \
    'SELECT count(*) FROM support_tickets')

messages=$(db support_db -Atc \
    'SELECT count(*) FROM support_messages')

ticket_cols='"Id","OrderId","LicenseId","CustomerId","Category","Subject","Description","Status","AssignedRole","HandledBy","Version","CreatedAtUtc","UpdatedAtUtc"'

message_cols='"Id","TicketId","SenderId","SenderRole","Content","CreatedAtUtc"'

if [[ "$tickets" == "0" && "$messages" == "0" ]]; then

    echo "[COPY] Migrating legacy Support data..."

    db order_db -c \
        "COPY (SELECT $ticket_cols FROM support_tickets ORDER BY \"Id\") TO STDOUT WITH CSV" \
        > "$tmp/tickets.csv"

    db order_db -c \
        "COPY (SELECT $message_cols FROM support_messages ORDER BY \"Id\") TO STDOUT WITH CSV" \
        > "$tmp/messages.csv"

    {
        echo 'BEGIN;'

        echo "COPY support_tickets ($ticket_cols) FROM STDIN WITH CSV;"
        cat "$tmp/tickets.csv"
        printf '\\.\n'

        echo "COPY support_messages ($message_cols) FROM STDIN WITH CSV;"
        cat "$tmp/messages.csv"
        printf '\\.\n'

        echo 'COMMIT;'
    } | db support_db

else
    echo "[CHECK] Support database already contains data."
    echo "[CHECK] Existing data will not be overwritten."
fi

# Check immutable ticket fields.
# Status, assignment and timestamps may have changed after cutover.

ticket_query='
SELECT md5(
    (to_jsonb(t) - ARRAY[
        '\''Status'\'',
        '\''HandledBy'\'',
        '\''Version'\'',
        '\''UpdatedAtUtc'\''
    ])::text
)
FROM support_tickets t;
'

message_query='
SELECT md5(to_jsonb(m)::text)
FROM support_messages m;
'

for table in tickets messages; do

    if [[ "$table" == "tickets" ]]; then
        sql="$ticket_query"
    else
        sql="$message_query"
    fi

    db order_db -Atc "$sql" |
        LC_ALL=C sort > "$tmp/$table.old"

    db support_db -Atc "$sql" |
        LC_ALL=C sort > "$tmp/$table.new"

    LC_ALL=C comm -23 \
        "$tmp/$table.old" \
        "$tmp/$table.new" \
        > "$tmp/$table.missing"

    if [[ -s "$tmp/$table.missing" ]]; then
        echo "[STOP] Missing or inconsistent legacy $table."
        echo "Legacy tables must not be removed."
        exit 1
    fi

    echo "[PASS] Legacy $table verified."

done

echo "[PASS] Support data migration verified."

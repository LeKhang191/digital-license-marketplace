#!/usr/bin/env bash
# Read-only DLM terminal dashboard. No ANSI colors, no external dependencies.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1
umask 077
compose=(docker compose --ansi never -f docker-compose.yml)
if [[ ${CODESPACES:-false} == true ]]; then
    compose+=(-f docker-compose.auth-proxy.yml)
fi
# Explicit psql formatting keeps exports consistent even without a TTY.
query() {
    local db=$1 sql=$2
    "${compose[@]}" exec -T postgres sh -c 'PGOPTIONS="-c default_transaction_read_only=on -c statement_timeout=15000" psql -X -q -v ON_ERROR_STOP=1 -P linestyle=unicode -P border=2 -P pager=off -P null=- -U "$POSTGRES_USER" -d "$1" -c "$2"' sh "$db" "$sql"
}
section() { printf '\n%s\n%s\n' '============================================================' "$*"; }
# Fixed allowlist: never display credentials or raw payloads.

# RABBITMQ DASHBOARD V2
# Read-only monitoring, Unicode tables, no ANSI colors.

rabbit_table() {
    "${compose[@]}" exec -T rabbitmq \
        rabbitmqctl -q "$@" --formatter json |
    python3 -c '
import json
import sys

rows = json.load(sys.stdin)

if not rows:
    print("(0 records)")
    sys.exit(0)

keys = list(rows[0])

aliases = {
    "name": "Name",
    "messages_ready": "Ready",
    "messages_unacknowledged": "Unack",
    "consumers": "Consumers",
    "source_name": "Exchange",
    "destination_name": "Destination",
    "destination_kind": "Kind",
    "routing_key": "Routing Key",
    "consumer_tag": "Consumer",
    "queue_name": "Queue",
    "ack_required": "ACK",
    "prefetch_count": "Prefetch"
}

headers = [aliases.get(k, k.title()) for k in keys]

def clean(value):
    if value is None or value == "":
        return "-"
    return str(value).replace("\n", " ").replace("\r", " ")

data = [[clean(row.get(k)) for k in keys] for row in rows]

widths = [
    max(len(headers[i]), max(len(row[i]) for row in data))
    for i in range(len(keys))
]

def border(left, middle, right):
    return left + middle.join("─" * (w + 2) for w in widths) + right

def line(row):
    return "│ " + " │ ".join(
        value.ljust(widths[i])
        for i, value in enumerate(row)
    ) + " │"

print(border("┌", "┬", "┐"))
print(line(headers))
print(border("├", "┼", "┤"))

for row in data:
    print(line(row))

print(border("└", "┴", "┘"))
print(f"({len(data)} records)")
'
}

rabbit_queues() {
    section 'RabbitMQ / All Queues'
    printf '%s\n' 'Ready: waiting messages' \
        'Unack: messages being processed' \
        'Consumers: active workers'
    rabbit_table list_queues \
        name messages_ready messages_unacknowledged consumers
}

rabbit_filtered() {
    local mode=$1

    "${compose[@]}" exec -T rabbitmq \
        rabbitmqctl -q list_queues \
        name messages_ready messages_unacknowledged consumers \
        --formatter json |
    python3 -c '
import json, sys
mode = sys.argv[1]
rows = json.load(sys.stdin)
rows = [
    r for r in rows
    if r["name"].endswith(".dead") == (mode == "dead")
]

headers = ["Queue", "Ready", "Unack", "Consumers"]
data = [
    [str(r[k]) for k in (
        "name", "messages_ready",
        "messages_unacknowledged", "consumers"
    )] for r in rows
]

widths = [
    max([len(h)] + [len(r[i]) for r in data])
    for i, h in enumerate(headers)
]

def border(a,b,c):
    return a + b.join("─"*(w+2) for w in widths) + c

def line(row):
    return "│ " + " │ ".join(
        x.ljust(widths[i]) for i,x in enumerate(row)
    ) + " │"

print(border("┌","┬","┐"))
print(line(headers))
print(border("├","┼","┤"))
for row in data:
    print(line(row))
print(border("└","┴","┘"))
print(f"({len(rows)} queues)")
' "$mode"
}

rabbit_overview() {
    section 'RabbitMQ / Overview'

    "${compose[@]}" exec -T rabbitmq \
        rabbitmqctl -q list_queues \
        name messages_ready messages_unacknowledged consumers \
        --formatter json |
    python3 -c '
import json, sys
rows = json.load(sys.stdin)
dead = [r for r in rows if r["name"].endswith(".dead")]

stats = [
    ("Total queues", len(rows)),
    ("Application queues", len(rows)-len(dead)),
    ("Dead-letter queues", len(dead)),
    ("Total ready messages", sum(r["messages_ready"] for r in rows)),
    ("Unacknowledged", sum(r["messages_unacknowledged"] for r in rows)),
    ("Active consumers", sum(r["consumers"] for r in rows)),
    ("Dead-letter messages", sum(r["messages_ready"] for r in dead))
]

print("┌──────────────────────────┬──────────┐")
print("│ Metric                   │ Value    │")
print("├──────────────────────────┼──────────┤")
for k,v in stats:
    print(f"│ {k:<24} │ {v:>8} │")
print("└──────────────────────────┴──────────┘")
'
}

rabbit_report() {
    local file rc=0
    mkdir -p reports || return 1
    file=$(mktemp "reports/rabbitmq-$(date +%Y%m%d-%H%M%S)-XXXXXX.txt") || return 1

    {
        printf 'RabbitMQ Read-only Report\n'
        date
        for cmd in rabbit-overview rabbit-active rabbit-dead \
            rabbit-exchanges rabbit-bindings rabbit-consumers \
            rabbit-connections rabbit-channels; do
            run "$cmd" || {
                printf '[ERROR] %s\n' "$cmd"
                rc=1
            }
        done
    } > "$file" 2>&1

    printf 'Report: %s\n' "$file"
    return "$rc"
}

rabbit_menu() {
    local choice
    while true; do
        section 'SYSTEM MANAGEMENT / RABBITMQ'
        cat <<'MENU'
01  Overview - Tong quan RabbitMQ
02  Application Queues - Queue xu ly nghiep vu
03  Dead Letter Queues - Message xu ly that bai
04  Exchanges - Phan phoi su kien
05  Bindings - Dinh tuyen su kien
06  Consumers - Worker xu ly message
07  Connections - Ket noi microservice
08  Channels - Kenh giao tiep RabbitMQ
09  Export Report - Xuat bao cao TXT
00  Back
MENU

        read -r -p 'Option: ' choice || return 0

        case "$choice" in
            1|01) run rabbit-overview ;;
            2|02) run rabbit-active ;;
            3|03) run rabbit-dead ;;
            4|04) run rabbit-exchanges ;;
            5|05) run rabbit-bindings ;;
            6|06) run rabbit-consumers ;;
            7|07) run rabbit-connections ;;
            8|08) run rabbit-channels ;;
            9|09) run rabbit-export ;;
            0|00) return 0 ;;
            *) continue ;;
        esac

        read -r -p 'Press Enter to return...' _ || return 0
    done
}

run() {
    case "$1" in
        products)
            section Products
            printf "%s\n" 'Database: catalog_db | Table: products' 'UUID day du; gia VND'
            query catalog_db 'SELECT "Id", "Name", "Category", "Price", "IsActive", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)" FROM public.products ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        active-products)
            section 'Active Products'
            printf "%s\n" 'Database: catalog_db | Table: products' 'San pham dang hoat dong; gia VND'
            query catalog_db 'SELECT "Id", "Name", "Category", "Price" FROM public.products WHERE "IsActive" ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        inactive-products)
            section 'Inactive Products'
            printf "%s\n" 'Database: catalog_db | Table: products' 'San pham ngung hoat dong; gia VND'
            query catalog_db 'SELECT "Id", "Name", "Category", "Price" FROM public.products WHERE NOT "IsActive" ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        categories)
            section 'Category Statistics'
            printf "%s\n" 'Database: catalog_db | Table: products' 'Dem san pham theo danh muc'
            query catalog_db 'SELECT "Category", count(*) AS "Total", count(*) FILTER (WHERE "IsActive") AS "Active" FROM public.products GROUP BY 1 ORDER BY 1;' ;;
        licenses)
            section 'License Inventory'
            printf "%s\n" 'Database: license_db | Table: license_keys' 'Ton kho theo san pham va trang thai'
            query license_db 'SELECT "ProductId", "Status", count(*) AS "Qty" FROM public.license_keys GROUP BY 1,2 ORDER BY 1,2;' ;;
        license-records)
            section 'License Records'
            printf "%s\n" 'Database: license_db | Table: license_keys' 'Khong hien key hoac fingerprint'
            query license_db 'SELECT "Id", "ProductId", "Status", "OrderId", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("AssignedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "AssignedAt (VN)" FROM public.license_keys ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        allocations)
            section 'Allocation Results'
            printf "%s\n" 'Database: license_db | Table: allocation_results' 'Ket qua cap phat; khong hien payload'
            query license_db 'SELECT "OrderId", "Status", "RetryCount", to_char("ProcessedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "ProcessedAt (VN)", to_char("PublishedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "PublishedAt (VN)" FROM public.allocation_results ORDER BY "ProcessedAtUtc" DESC LIMIT 30;' ;;
        replacements)
            section 'Replacement History'
            printf "%s\n" 'Database: license_db | Table: license_replacements' 'Lich su doi license'
            query license_db 'SELECT "TicketId", "OrderId", "OldLicenseId", "NewLicenseId", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)" FROM public.license_replacements ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        refund-guards)
            section 'Refund Guards'
            printf "%s\n" 'Database: license_db | Table: license_refund_guards' 'Bao ve hoan tien'
            query license_db 'SELECT "OrderId", "RequestId", "Mode", "Status", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("FinalizedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "FinalizedAt (VN)" FROM public.license_refund_guards ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        orders)
            section 'All Orders'
            printf "%s\n" 'Database: order_db | Table: orders' 'Tong tien VND; khong hien email'
            query order_db 'SELECT "Id", "TotalAmount", "Status", "PaymentMethod", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("PaidAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "PaidAt (VN)" FROM public.orders ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        order-items)
            section 'Order Items'
            printf "%s\n" 'Database: order_db | Table: order_items, orders' 'Chi tiet san pham trong don; gia VND'
            query order_db 'SELECT i."OrderId", i."ProductId", i."Price" FROM public.order_items i JOIN public.orders o ON o."Id"=i."OrderId" ORDER BY o."CreatedAtUtc" DESC, i."Id" LIMIT 30;' ;;
        order-stats)
            section 'Order Statistics'
            printf "%s\n" 'Database: order_db | Table: orders' 'Dem don theo trang thai'
            query order_db 'SELECT "Status", count(*) AS "Total" FROM public.orders GROUP BY 1 ORDER BY 1;' ;;
        fulfillment)
            section 'Fulfillment Results'
            printf "%s\n" 'Database: order_db | Table: fulfillment_results' 'Ket qua giao hang'
            query order_db 'SELECT "OrderId", "Status", to_char("ProcessedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "ProcessedAt (VN)" FROM public.fulfillment_results ORDER BY "ProcessedAtUtc" DESC LIMIT 30;' ;;
        order-outbox)
            section 'Order Outbox'
            printf "%s\n" 'Database: order_db | Table: outbox_messages' 'Metadata su kien'
            query order_db 'SELECT "Id", "RoutingKey", "RetryCount", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("PublishedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "PublishedAt (VN)" FROM public.outbox_messages ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        payments)
            section Payments
            printf "%s\n" 'Database: payment_db | Table: payments' 'Thanh toan VND'
            query payment_db 'SELECT "Id", "OrderId", "Amount", "Status", "PaymentMethod", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("PaidAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "PaidAt (VN)" FROM public.payments ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        wallets)
            section Wallets
            printf "%s\n" 'Database: payment_db | Table: wallets' 'So du VND; khong hien CustomerId'
            query payment_db 'SELECT "Id", "Balance", to_char("UpdatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "UpdatedAt (VN)" FROM public.wallets ORDER BY "UpdatedAtUtc" DESC LIMIT 30;' ;;
        wallet-transactions)
            section 'Wallet Transactions'
            printf "%s\n" 'Database: payment_db | Table: wallet_transactions' 'Bien dong VND'
            query payment_db 'SELECT "WalletId", "Type", "Amount", "BalanceAfter", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)" FROM public.wallet_transactions ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        refunds)
            section 'Refund History'
            printf "%s\n" 'Database: payment_db | Table: refunds' 'Hoan tien VND'
            query payment_db 'SELECT "Id", "PaymentId", "Amount", "Status", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("CompletedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CompletedAt (VN)" FROM public.refunds ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        refund-operations)
            section 'Refund Operations'
            printf "%s\n" 'Database: order_db | Table: refund_operations' 'Tien trinh hoan tien'
            query order_db 'SELECT "OrderId", "Mode", "Status", "RetryCount", to_char("UpdatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "UpdatedAt (VN)" FROM public.refund_operations ORDER BY "UpdatedAtUtc" DESC LIMIT 30;' ;;
        payment-outbox)
            section 'Payment Outbox'
            printf "%s\n" 'Database: payment_db | Table: outbox_messages' 'Metadata su kien'
            query payment_db 'SELECT "Id", "RoutingKey", "RetryCount", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("PublishedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "PublishedAt (VN)" FROM public.outbox_messages ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        tickets)
            section 'Support Tickets'
            printf "%s\n" 'Database: support_db | Table: support_tickets' 'Metadata ticket; khong hien noi dung'
            query support_db 'SELECT "Id", "OrderId", "Category", "Status", "AssignedRole", to_char("UpdatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "UpdatedAt (VN)" FROM public.support_tickets ORDER BY "UpdatedAtUtc" DESC LIMIT 30;' ;;
        ticket-stats)
            section 'Ticket Statistics'
            printf "%s\n" 'Database: support_db | Table: support_tickets' 'Dem ticket theo trang thai'
            query support_db 'SELECT "Status", count(*) AS "Total" FROM public.support_tickets GROUP BY 1 ORDER BY 1;' ;;
        messages)
            section 'Support Messages'
            printf "%s\n" 'Database: support_db | Table: support_messages' 'Metadata; khong hien noi dung'
            query support_db 'SELECT "TicketId", "SenderRole", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)" FROM public.support_messages ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        notifications)
            section Notifications
            printf "%s\n" 'Database: notification_db | Table: notification_messages' 'Metadata; khong hien email'
            query notification_db 'SELECT "OrderId", "Type", "Status", "RetryCount", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("SentAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "SentAt (VN)" FROM public.notification_messages ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        kinguin)
            section 'Kinguin Import History'
            printf "%s\n" 'Database: license_db | Table: kinguin_mock_orders' 'MOCK ONLY; chi phi mo phong EUR'
            query license_db 'SELECT "Id", "SupplierProductId", "Quantity", "TotalCost", "Status", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)" FROM public.kinguin_mock_orders ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        kinguin-stats)
            section 'Kinguin Statistics'
            printf "%s\n" 'Database: license_db | Table: kinguin_mock_orders' 'MOCK ONLY; tong chi phi mo phong EUR'
            query license_db 'SELECT "Status", count(*) AS "Orders", sum("Quantity") AS "Requested qty", sum("TotalCost") AS "Simulated EUR" FROM public.kinguin_mock_orders GROUP BY 1 ORDER BY 1;' ;;
        jira)
            section 'Jira Linked Tickets'
            printf "%s\n" 'Database: support_db | Table: support_external_links' 'Lien ket Jira; khong hien comment'
            query support_db 'SELECT "TicketId", "IssueKey", "State", "ExternalStatus", to_char("CreatedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "CreatedAt (VN)", to_char("SyncedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"', '"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "SyncedAt (VN)" FROM public.support_external_links ORDER BY "CreatedAtUtc" DESC LIMIT 30;' ;;
        jira-sync)
            section 'Jira Sync Status'
            printf "%s\n" 'Database: support_db | Table: support_external_links' 'Thoi diem dong bo va trang thai'
            query support_db 'SELECT "IssueKey", "State", "ExternalStatus", to_char("SyncedAtUtc" AT TIME ZONE '"'"'Asia/Ho_Chi_Minh'"'"','"'"'DD/MM/YYYY HH24:MI:SS'"'"') AS "Synced (VN)" FROM public.support_external_links ORDER BY "SyncedAtUtc" DESC NULLS LAST LIMIT 30;' ;;
        docker) section 'Docker Containers'; "${compose[@]}" ps --format 'table {{.Service}}\t{{.Status}}' ;;
        rabbitmq) rabbit_queues ;;
        rabbit-menu) rabbit_menu ;;
        rabbit-overview) rabbit_overview ;;
        rabbit-active)
            section 'RabbitMQ / Application Queues'
            rabbit_filtered active ;;
        rabbit-dead)
            section 'RabbitMQ / Dead Letter Queues'
            rabbit_filtered dead ;;
        rabbit-exchanges)
            section 'RabbitMQ / Exchanges'
            rabbit_table list_exchanges name type durable ;;
        rabbit-bindings)
            section 'RabbitMQ / Bindings'
            rabbit_table list_bindings \
                source_name destination_name destination_kind routing_key ;;
        rabbit-consumers)
            section 'RabbitMQ / Consumers'
            rabbit_table list_consumers \
                queue_name consumer_tag ack_required prefetch_count active ;;
        rabbit-connections)
            section 'RabbitMQ / Connections'
            rabbit_table list_connections \
                name state channels protocol ;;
        rabbit-channels)
            section 'RabbitMQ / Channels'
            rabbit_table list_channels \
                number consumer_count \
                messages_unacknowledged prefetch_count ;;
        rabbit-export) rabbit_report ;;
        databases)
            section 'Database Overview'
            query postgres "SELECT datname AS \"Database\", pg_size_pretty(pg_database_size(datname)) AS \"Size\" FROM pg_database WHERE NOT datistemplate ORDER BY datname;" || return 1
            local db
            for db in catalog_db license_db order_db payment_db support_db notification_db; do
                printf '\n%s\n' "$db"
                query "$db" "SELECT count(*) AS \"Public tables\" FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';" || return 1
            done ;;
        system) run docker && run rabbitmq && run databases ;;
        kinguin-config)
            section 'Kinguin Configuration'
            "${compose[@]}" exec -T license-vault sh -c 'printf "Mode: %s\n" "${Kinguin__Mode:-Disabled}"' ;;
        kinguin-mapping)
            section 'Kinguin Product Mapping'
            "${compose[@]}" exec -T license-vault sh -c 'printf "Local UUID = Supplier ID\n%s=%s\n" "$Kinguin__LocalProductId" "$Kinguin__ProductId"; printf "%s\n" "$Kinguin__ProductMappings" | tr ";" "\n"' ;;
        export) export_report all ;;
        export-system) export_report 1 ;;
        export-inventory) export_report 2 3 ;;
        export-commerce) export_report 4 5 ;;
        export-integrations) export_report 7 ;;
        *) printf 'Unknown option: %s\n' "$1" >&2; return 2 ;;
    esac
}

# Menu metadata: category | command | title | description.
entries=(
    '1|system|System Overview|Container, queues va database'
    '1|docker|Docker Containers|Trang thai container'
    '1|rabbitmq|RabbitMQ Management|Overview, queues, exchanges, bindings va workers'
    '1|databases|Database Overview|Dung luong va so bang public'
    '2|products|Products|UUID day du; gia VND'
    '2|active-products|Active Products|San pham dang hoat dong; gia VND'
    '2|inactive-products|Inactive Products|San pham ngung hoat dong; gia VND'
    '2|categories|Category Statistics|Dem san pham theo danh muc'
    '3|licenses|License Inventory|Ton kho theo san pham va trang thai'
    '3|license-records|License Records|Khong hien key hoac fingerprint'
    '3|allocations|Allocation Results|Ket qua cap phat; khong hien payload'
    '3|replacements|Replacement History|Lich su doi license'
    '3|refund-guards|Refund Guards|Bao ve hoan tien'
    '4|orders|All Orders|Tong tien VND; khong hien email'
    '4|order-items|Order Items|Chi tiet san pham trong don; gia VND'
    '4|order-stats|Order Statistics|Dem don theo trang thai'
    '4|fulfillment|Fulfillment Results|Ket qua giao hang'
    '4|order-outbox|Order Outbox|Metadata su kien'
    '5|payments|Payments|Thanh toan VND'
    '5|wallets|Wallets|So du VND; khong hien CustomerId'
    '5|wallet-transactions|Wallet Transactions|Bien dong VND'
    '5|refunds|Refund History|Hoan tien VND'
    '5|refund-operations|Refund Operations|Tien trinh hoan tien'
    '5|payment-outbox|Payment Outbox|Metadata su kien'
    '6|tickets|Support Tickets|Metadata ticket; khong hien noi dung'
    '6|ticket-stats|Ticket Statistics|Dem ticket theo trang thai'
    '6|messages|Support Messages|Metadata; khong hien noi dung'
    '6|notifications|Notifications|Metadata; khong hien email'
    '7|kinguin-config|Kinguin Configuration|Chi hien mode; khong hien API key'
    '7|kinguin-mapping|Kinguin Product Mapping|UUID va supplier ID tu cau hinh runtime'
    '7|kinguin|Kinguin Import History|MOCK ONLY; chi phi mo phong EUR'
    '7|kinguin-stats|Kinguin Statistics|MOCK ONLY; tong chi phi mo phong EUR'
    '7|jira|Jira Linked Tickets|Lien ket Jira; khong hien comment'
    '7|jira-sync|Jira Sync Status|Thoi diem dong bo va trang thai'
    '8|export|Export All|Tat ca bao cao'
    '8|export-system|Export System|Ha tang'
    '8|export-inventory|Export Inventory|Catalog va license'
    '8|export-commerce|Export Commerce|Order va payment'
    '8|export-integrations|Export Integrations|Kinguin va Jira'
)
categories=(
    'System Management|He thong va ha tang'
    'Catalog Management|Danh muc san pham'
    'License Management|Kho va cap phat license'
    'Order Management|Don hang va giao hang'
    'Payment & Wallet|Thanh toan, vi va hoan tien'
    'Customer Support|Ho tro va thong bao'
    'External Integrations|Kinguin va Jira'
    'Reports & Export|Xuat bao cao TXT'
)

export_report() {
    local file row c k t n group selected rc=0
    mkdir -p reports || return 1
    file=$(mktemp "reports/dlm-report-$(date +%Y%m%d-%H%M%S)-XXXXXX.txt") || return 1
    {
        printf 'DLM READ-ONLY REPORT | %s\n' "$(date -u +%FT%TZ)"
        for row in "${entries[@]}"; do
            IFS='|' read -r c k t n <<< "$row"
            [[ $c == 8 || $k == system ]] && continue
            selected=false
            for group in "$@"; do [[ $group == all || $group == "$c" ]] && selected=true; done
            if $selected; then run "$k" || { printf '[ERROR] %s\n' "$k"; rc=1; }; fi
        done
    } > "$file" 2>&1
    printf 'Report: %s\n' "$file"
    return "$rc"
}
help() {
    printf 'Usage: bash scripts/dashboard.sh [command]\nNo command opens the menu. Read-only; reports contain operational metadata.\n'
    local row c k t n
    for row in "${entries[@]}"; do IFS='|' read -r c k t n <<< "$row"; printf '  %-23s %s\n' "$k" "$t"; done
}
menu() {
    local choice sub row c k t n i label note
    local -a keys
    while true; do
        section 'DLM CONTROL CENTER | READ-ONLY'
        for i in "${!categories[@]}"; do
            IFS='|' read -r label note <<< "${categories[i]}"
            printf '%02d  %s — %s\n' "$((i+1))" "${label^^}" "$note"
        done
        printf '00  Exit\n'
        read -r -p 'Category: ' choice || return 0
        [[ $choice == 0 || $choice == 00 ]] && return 0
        [[ $choice =~ ^0?[1-8]$ ]] || continue
        choice=${choice#0}
        while true; do
            section "${categories[choice-1]%%|*}"
            keys=(); i=0
            for row in "${entries[@]}"; do
                IFS='|' read -r c k t n <<< "$row"
                [[ $c == "$choice" ]] || continue
                keys+=("$k"); i=$((i+1)); printf '%02d  %s — %s\n' "$i" "$t" "$n"
            done
            printf '00  Back\n'
            read -r -p 'Option: ' sub || return 0
            [[ $sub == 0 || $sub == 00 ]] && break
            [[ $sub =~ ^0?[1-9]$ ]] || continue
            sub=${sub#0}; (( sub <= ${#keys[@]} )) || continue
            if [[ ${keys[sub-1]} == rabbitmq ]]; then
                rabbit_menu
                continue
            fi
            run "${keys[sub-1]}" || printf '[ERROR] Query failed.\n' >&2
            read -r -p 'Press Enter to return...' _ || return 0
        done
    done
}
case "${1:-}" in
    --help|-h) help ;;
    '') menu ;;
    *) run "$1" ;;
esac

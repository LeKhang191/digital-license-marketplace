
#!/usr/bin/env bash

set -Eeuo pipefail

cd "$(dirname "$0")/.."

API="${API_URL:-http://localhost:8000/api}"

KEYCLOAK="${KEYCLOAK_URL:-http://localhost:8081}"

REALM="marketplace"
CLIENT_ID="marketplace-web"

PASSED=0
FAILED=0

RUN_ID="$(openssl rand -hex 8)"

TMP_DIR="$(mktemp -d)"

trap 'rm -rf "$TMP_DIR"' EXIT

BODY="$TMP_DIR/response.json"

EMPTY_UUID="00000000-0000-0000-0000-000000000000"

FAKE_UUID="11111111-1111-4111-8111-111111111111"

echo "=========================================="
echo " License Vault Integration & Security Test"
echo "=========================================="
echo "Run ID: $RUN_ID"
echo ""

for cmd in curl jq openssl docker; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "Missing dependency: $cmd"
        exit 1
    fi
done

pass() {
    PASSED=$((PASSED + 1))
    printf '\033[32m[PASS]\033[0m %s\n' "$1"
}

fail() {
    FAILED=$((FAILED + 1))
    printf '\033[31m[FAIL]\033[0m %s\n' "$1"
}

check() {
    local name="$1"
    shift

    if "$@"; then
        pass "$name"
    else
        fail "$name"
    fi
}

require_test() {
    if ! "$@"; then
        echo "[STOP] Required test data is missing."
        exit 1
    fi
}

login() {
    local username="$1"
    local password
    local token
    local response

    read -rsp "Password for $username: " password
    echo >&2

    response=$(curl -fsS --max-time 20 \
        -X POST \
        "$KEYCLOAK/realms/$REALM/protocol/openid-connect/token" \
        --data-urlencode "grant_type=password" \
        --data-urlencode "client_id=$CLIENT_ID" \
        --data-urlencode "username=$username" \
        --data-urlencode "password=$password")

    token=$(jq -r '.access_token // empty' <<< "$response")

    if [[ -z "$token" ]]; then
        echo "Login failed: $username" >&2
        return 1
    fi

    printf '%s' "$token"
}

request() {
    local method="$1"
    local path="$2"
    local token="${3:-}"
    shift 3

    local args=()

    if [[ -n "$token" ]]; then
        args+=(-H "Authorization: Bearer $token")
    fi

    curl -sS \
        --max-time 60 \
        -o "$BODY" \
        -w '%{http_code}' \
        -X "$method" \
        "${args[@]}" \
        "$@" \
        "$API$path"
}

expect_status() {
    local name="$1"
    local expected="$2"

    shift 2

    local actual

    actual=$(request "$@")

    if [[ "$actual" == "$expected" ]]; then
        pass "$name (HTTP $actual)"
    else
        fail "$name (expected $expected, got $actual)"
    fi
}

json_equals() {
    jq -e "$1" "$BODY" >/dev/null 2>&1
}

query_db() {
    docker compose exec -T postgres \
        psql -X -U app -d license_db \
        -v ON_ERROR_STOP=1 \
        -Atc "$1"
}

echo "===== Selecting test product ====="

PRODUCT_ID=$(curl -fsS \
    "$API/catalog/products?search=License%20Vault%20Test&pageSize=100" \
    | jq -r '.items[]
        | select(.slug == "license-vault-test")
        | .id' | head -n 1)

if [[ -z "$PRODUCT_ID" ]]; then
    echo "Create an active product with slug:"
    echo "license-vault-test"
    exit 1
fi

echo "Test product: $PRODUCT_ID"
echo ""

echo "===== Authentication ====="

CUSTOMER_TOKEN=$(login test-user1)

MANAGER_TOKEN=$(login catalog_manager)

COORDINATOR_TOKEN=$(login license_coordinator)

AUDITOR_TOKEN=$(login transaction_auditor)

echo "All accounts logged in."
echo ""

PROTECTED_PAYLOAD=$(jq -n \
    --arg id "$PRODUCT_ID" \
    --arg key "DEMO-TEST-$RUN_ID-PROTECTED" \
    '{productId: $id, keys: [$key]}')

printf 'DEMO-TEST-%s-PROTECTED-FILE\n' "$RUN_ID" \
    > "$TMP_DIR/protected.txt"

test_protected_routes() {
    local role="$1"
    local expected="$2"
    local token="$3"

    expect_status "$role: inventory" \
        "$expected" GET \
        "/licenses/inventory" "$token"

    expect_status "$role: product licenses" \
        "$expected" GET \
        "/licenses/products/$PRODUCT_ID" "$token"

    expect_status "$role: manual import" \
        "$expected" POST \
        "/licenses/import" "$token" \
        -H "Content-Type: application/json" \
        -d "$PROTECTED_PAYLOAD"

    expect_status "$role: file import" \
        "$expected" POST \
        "/licenses/import-file" "$token" \
        -F "productId=$PRODUCT_ID" \
        -F "file=@$TMP_DIR/protected.txt"

    expect_status "$role: disable license" \
        "$expected" PATCH \
        "/licenses/$FAKE_UUID/disable" "$token"
}

echo "===== Anonymous access ====="

test_protected_routes "Anonymous" 401 ""

echo ""
echo "===== Customer access ====="

test_protected_routes "Customer" 403 "$CUSTOMER_TOKEN"

echo ""
echo "===== Catalog Manager access ====="

test_protected_routes \
    "Catalog Manager" 403 "$MANAGER_TOKEN"

echo ""
echo "===== Transaction Auditor access ====="

test_protected_routes \
    "Transaction Auditor" 403 "$AUDITOR_TOKEN"

echo ""
echo "===== License Coordinator access ====="

expect_status "Coordinator: inventory" \
    200 GET "/licenses/inventory" \
    "$COORDINATOR_TOKEN"

expect_status "Coordinator: product licenses" \
    200 GET "/licenses/products/$PRODUCT_ID" \
    "$COORDINATOR_TOKEN"

INITIAL_IDS=$(jq -c '[.[].id]' "$BODY")

echo ""
echo "===== Manual import ====="

KEY_A="DEMO-TEST-$RUN_ID-A"
KEY_B="DEMO-TEST-$RUN_ID-B"

PAYLOAD=$(jq -n \
    --arg id "$PRODUCT_ID" \
    --arg a "$KEY_A" \
    --arg b "$KEY_B" \
    '{
        productId: $id,
        keys: [$a, $b, $b, "abc", ""]
    }')

expect_status "Manual import succeeds" \
    200 POST "/licenses/import" \
    "$COORDINATOR_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$PAYLOAD"

check "Manual import counters are correct" \
    json_equals \
    '.total == 5
     and .imported == 2
     and .duplicates == 1
     and .invalid == 2'

expect_status "Repeated manual import succeeds" \
    200 POST "/licenses/import" \
    "$COORDINATOR_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$PAYLOAD"

check "Repeated import does not create duplicates" \
    json_equals \
    '.imported == 0
     and .duplicates == 3
     and .invalid == 2'

expect_status "Empty key list is rejected" \
    400 POST "/licenses/import" \
    "$COORDINATOR_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$(jq -n --arg id "$PRODUCT_ID" \
        '{productId: $id, keys: []}')"

expect_status "Empty product ID is rejected" \
    400 POST "/licenses/import" \
    "$COORDINATOR_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$(jq -n --arg id "$EMPTY_UUID" \
        '{productId: $id, keys: ["DEMO-INVALID"]}')"

echo ""
echo "===== CSV and TXT import ====="

cat > "$TMP_DIR/licenses.csv" <<EOF
license_key
DEMO-TEST-$RUN_ID-CSV1
DEMO-TEST-$RUN_ID-CSV2
DEMO-TEST-$RUN_ID-CSV3
EOF

cat > "$TMP_DIR/licenses.txt" <<EOF
DEMO-TEST-$RUN_ID-TXT1
DEMO-TEST-$RUN_ID-TXT2
EOF

expect_status "CSV upload succeeds" \
    200 POST "/licenses/import-file" \
    "$COORDINATOR_TOKEN" \
    -F "productId=$PRODUCT_ID" \
    -F "file=@$TMP_DIR/licenses.csv"

check "CSV imports three keys" \
    json_equals \
    '.total == 3 and .imported == 3'

expect_status "TXT upload succeeds" \
    200 POST "/licenses/import-file" \
    "$COORDINATOR_TOKEN" \
    -F "productId=$PRODUCT_ID" \
    -F "file=@$TMP_DIR/licenses.txt"

check "TXT imports two keys" \
    json_equals \
    '.total == 2 and .imported == 2'

printf 'invalid file\n' > "$TMP_DIR/invalid.pdf"

expect_status "Unsupported file extension rejected" \
    400 POST "/licenses/import-file" \
    "$COORDINATOR_TOKEN" \
    -F "productId=$PRODUCT_ID" \
    -F "file=@$TMP_DIR/invalid.pdf"

printf 'license_key,extra\nDEMO-INVALID,value\n' \
    > "$TMP_DIR/invalid.csv"

expect_status "Invalid CSV structure rejected" \
    400 POST "/licenses/import-file" \
    "$COORDINATOR_TOKEN" \
    -F "productId=$PRODUCT_ID" \
    -F "file=@$TMP_DIR/invalid.csv"

echo ""
echo "===== Catalog integration ====="

expect_status "Nonexistent product rejected" \
    404 POST "/licenses/import" \
    "$COORDINATOR_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$(jq -n --arg id "$FAKE_UUID" \
        '{productId: $id, keys: ["DEMO-UNKNOWN"]}')"

expect_status "Valid active product accepts import" \
    200 POST "/licenses/import" \
    "$COORDINATOR_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$(jq -n \
        --arg id "$PRODUCT_ID" \
        --arg key "DEMO-TEST-$RUN_ID-CATALOG" \
        '{productId: $id, keys: [$key]}')"

echo ""
echo "===== Inventory and disable ====="

expect_status "Product license list succeeds" \
    200 GET \
    "/licenses/products/$PRODUCT_ID" \
    "$COORDINATOR_TOKEN"

check "License API does not expose secret fields" \
    json_equals \
    'type == "array" and all(.[];
      (keys | sort) ==
      (["id", "productId", "status",
        "createdAtUtc", "assignedAtUtc"] | sort))'

LICENSE_ID=$(jq -r \
    --argjson before "$INITIAL_IDS" \
    '[.[] |
        select(.status == "AVAILABLE") |
        select(.id as $id |
            ($before | index($id)) == null)
    ][0].id // empty' "$BODY")

require_test test -n "$LICENSE_ID"

# Only disable a newly added test license.
expect_status "Disable available license" \
    204 PATCH \
    "/licenses/$LICENSE_ID/disable" \
    "$COORDINATOR_TOKEN"

expect_status "Disable already disabled license rejected" \
    409 PATCH \
    "/licenses/$LICENSE_ID/disable" \
    "$COORDINATOR_TOKEN"

request GET \
    "/licenses/products/$PRODUCT_ID" \
    "$COORDINATOR_TOKEN" >/dev/null

check "Disabled status persists" \
    json_equals \
    ".[] | select(.id == \"$LICENSE_ID\") |
     .status == \"DISABLED\""

echo ""
echo "===== Concurrent import ====="

CONCURRENT_PAYLOAD=$(jq -n \
    --arg id "$PRODUCT_ID" \
    --arg key "DEMO-TEST-$RUN_ID-CONCURRENT" \
    '{productId: $id, keys: [$key]}')

for i in 1 2; do
    curl -sS --max-time 60 \
        -o "$TMP_DIR/concurrent-$i.json" \
        -w '%{http_code}' \
        -X POST "$API/licenses/import" \
        -H "Authorization: Bearer $COORDINATOR_TOKEN" \
        -H "Content-Type: application/json" \
        -d "$CONCURRENT_PAYLOAD" \
        > "$TMP_DIR/concurrent-$i.status" &
done

wait

if [[ "$(cat "$TMP_DIR/concurrent-1.status")" == "200" &&
      "$(cat "$TMP_DIR/concurrent-2.status")" == "200" ]] &&
    jq -se '
        (map(.imported) | add) == 1
        and
        (map(.duplicates) | add) == 1
    ' "$TMP_DIR/concurrent-1.json" \
      "$TMP_DIR/concurrent-2.json" >/dev/null 2>&1; then

    pass "Concurrent import creates exactly one key"
else
    fail "Concurrent duplicate protection"
fi

echo ""
echo "===== Database security ====="

PLAINTEXT_COUNT=$(query_db "
    SELECT count(*)
    FROM license_keys
    WHERE \"EncryptedKey\" LIKE '%DEMO-TEST-%';
")

check "No demo plaintext stored in ciphertext column" \
    test "$PLAINTEXT_COUNT" = "0"

DUPLICATE_COUNT=$(query_db '
    SELECT count(*)
    FROM (
        SELECT "KeyFingerprint"
        FROM license_keys
        GROUP BY "KeyFingerprint"
        HAVING count(*) > 1
    ) duplicates;
')

check "No duplicate fingerprints in database" \
    test "$DUPLICATE_COUNT" = "0"

INVALID_FINGERPRINTS=$(query_db "
    SELECT count(*)
    FROM license_keys
    WHERE \"KeyFingerprint\" !~ '^[0-9a-f]{64}$';
")

check "All fingerprints are valid SHA-256 hex" \
    test "$INVALID_FINGERPRINTS" = "0"

echo ""
echo "=========================================="
echo " TEST SUMMARY"
echo "=========================================="
echo " PASSED : $PASSED"
echo " FAILED : $FAILED"
echo " TOTAL  : $((PASSED + FAILED))"
echo "=========================================="

if [[ "$FAILED" -gt 0 ]]; then
    exit 1
fi

echo "All License Vault tests passed."

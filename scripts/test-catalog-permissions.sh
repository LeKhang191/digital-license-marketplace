
#!/usr/bin/env bash

set -euo pipefail

API="http://localhost:8000/api/catalog/products"
KEYCLOAK="http://localhost:8081"
REALM="marketplace"
CLIENT_ID="marketplace-web"

PASSED=0
FAILED=0

RESPONSE=$(mktemp)
PRODUCT_ID=""
MANAGER_TOKEN=""

cleanup() {
    if [[ -n "$PRODUCT_ID" && -n "$MANAGER_TOKEN" ]]; then
        curl -sS --max-time 15 -X DELETE \
            "$API/$PRODUCT_ID" \
            -H "Authorization: Bearer $MANAGER_TOKEN" \
            -o /dev/null || true
    fi

    rm -f "$RESPONSE"
}

trap cleanup EXIT

command -v jq >/dev/null || {
    echo "ERROR: jq is required."
    exit 1
}

get_token() {
    local username="$1"
    local password
    local result
    local token

    read -rsp "Password for $username: " password
    echo >&2

    result=$(curl -sS --max-time 15 -X POST \
        "$KEYCLOAK/realms/$REALM/protocol/openid-connect/token" \
        --data-urlencode "grant_type=password" \
        --data-urlencode "client_id=$CLIENT_ID" \
        --data-urlencode "username=$username" \
        --data-urlencode "password=$password")

    token=$(echo "$result" | jq -r '.access_token // empty')

    if [[ -z "$token" ]]; then
        echo "ERROR: Cannot authenticate $username" >&2
        echo "$result" | jq -r '.error_description // .error' >&2
        return 1
    fi

    printf '%s' "$token"
}

request() {
    local method="$1"
    local url="$2"
    local token="${3:-}"
    local body="${4:-}"

    local args=(
        -sS
        --max-time 15
        -o "$RESPONSE"
        -w "%{http_code}"
        -X "$method"
        "$url"
    )

    if [[ -n "$token" ]]; then
        args+=(-H "Authorization: Bearer $token")
    fi

    if [[ -n "$body" ]]; then
        args+=(
            -H "Content-Type: application/json"
            -d "$body"
        )
    fi

    curl "${args[@]}"
}

check() {
    local name="$1"
    local expected="$2"
    local actual="$3"

    if [[ "$expected" == "$actual" ]]; then
        printf '[PASS] %s (%s)\n' "$name" "$actual"
        PASSED=$((PASSED + 1))
    else
        printf '[FAIL] %s: expected %s, got %s\n' \
            "$name" "$expected" "$actual"
        cat "$RESPONSE"
        echo
        FAILED=$((FAILED + 1))
    fi
}

echo "========================================"
echo " CATALOG ROLE PERMISSION TEST"
echo "========================================"

CUSTOMER_TOKEN=$(get_token test-user1)
MANAGER_TOKEN=$(get_token catalog_manager)
LICENSE_TOKEN=$(get_token license_coordinator)
AUDITOR_TOKEN=$(get_token transaction_auditor)

SLUG="rbac-test-$(date +%s)-$$"

BODY=$(jq -n --arg slug "$SLUG" '{
    name: "RBAC Test Product",
    slug: $slug,
    description: "Temporary security test",
    category: "Game",
    price: 100000,
    isActive: true
}')

HIDDEN_BODY=$(echo "$BODY" | jq '.isActive = false')

echo
echo "===== PUBLIC ACCESS ====="

check "Anonymous list products" 200 \
    "$(request GET "$API")"

check "Anonymous manage products" 401 \
    "$(request GET "$API/manage")"

check "Anonymous create product" 401 \
    "$(request POST "$API" "" "$BODY")"

echo
echo "===== CUSTOMER PERMISSIONS ====="

check "Customer list products" 200 \
    "$(request GET "$API" "$CUSTOMER_TOKEN")"

check "Customer manage products" 403 \
    "$(request GET "$API/manage" "$CUSTOMER_TOKEN")"

check "Customer create product" 403 \
    "$(request POST "$API" "$CUSTOMER_TOKEN" "$BODY")"

echo
echo "===== LICENSE COORDINATOR PERMISSIONS ====="

check "License Coordinator manage products" 403 \
    "$(request GET "$API/manage" "$LICENSE_TOKEN")"

check "License Coordinator create product" 403 \
    "$(request POST "$API" "$LICENSE_TOKEN" "$BODY")"

echo
echo "===== TRANSACTION AUDITOR PERMISSIONS ====="

check "Transaction Auditor manage products" 403 \
    "$(request GET "$API/manage" "$AUDITOR_TOKEN")"

check "Transaction Auditor create product" 403 \
    "$(request POST "$API" "$AUDITOR_TOKEN" "$BODY")"

echo
echo "===== CATALOG MANAGER PERMISSIONS ====="

check "Catalog Manager manage products" 200 \
    "$(request GET "$API/manage" "$MANAGER_TOKEN")"

STATUS=$(request POST "$API" "$MANAGER_TOKEN" "$BODY")
check "Catalog Manager create product" 201 "$STATUS"

if [[ "$STATUS" != "201" ]]; then
    echo "Cannot continue without a test product."
    exit 1
fi

PRODUCT_ID=$(jq -r '.id // empty' "$RESPONSE")

if [[ -z "$PRODUCT_ID" ]]; then
    echo "ERROR: Product ID not found."
    exit 1
fi

echo "Test product ID: $PRODUCT_ID"

check "Public view product detail" 200 \
    "$(request GET "$API/$PRODUCT_ID")"

echo
echo "===== FORBIDDEN UPDATE AND DELETE ====="

check "Customer update product" 403 \
    "$(request PUT "$API/$PRODUCT_ID" "$CUSTOMER_TOKEN" "$BODY")"

check "Customer delete product" 403 \
    "$(request DELETE "$API/$PRODUCT_ID" "$CUSTOMER_TOKEN")"

check "License Coordinator update product" 403 \
    "$(request PUT "$API/$PRODUCT_ID" "$LICENSE_TOKEN" "$BODY")"

check "License Coordinator delete product" 403 \
    "$(request DELETE "$API/$PRODUCT_ID" "$LICENSE_TOKEN")"

check "Transaction Auditor update product" 403 \
    "$(request PUT "$API/$PRODUCT_ID" "$AUDITOR_TOKEN" "$BODY")"

check "Transaction Auditor delete product" 403 \
    "$(request DELETE "$API/$PRODUCT_ID" "$AUDITOR_TOKEN")"

echo
echo "===== PRODUCT VISIBILITY ====="

check "Catalog Manager hide product" 200 \
    "$(request PUT "$API/$PRODUCT_ID" "$MANAGER_TOKEN" "$HIDDEN_BODY")"

check "Public cannot view hidden product" 404 \
    "$(request GET "$API/$PRODUCT_ID")"

check "Customer cannot view hidden product" 404 \
    "$(request GET "$API/$PRODUCT_ID" "$CUSTOMER_TOKEN")"

check "Public list request" 200 \
    "$(request GET "$API?search=RBAC%20Test%20Product&pageSize=100")"

if jq -e --arg id "$PRODUCT_ID" \
    '(.items | type == "array") and
    ([.items[] | select(.id == $id)] | length == 0)' \
    "$RESPONSE" >/dev/null; then
    check "Hidden product excluded from public list" true true
else
    check "Hidden product excluded from public list" true false
fi

check "Manager list request" 200 \
    "$(request GET "$API/manage?search=RBAC%20Test%20Product&pageSize=100" "$MANAGER_TOKEN")"

if jq -e --arg id "$PRODUCT_ID" \
    '.items | any(.id == $id and .isActive == false)' \
    "$RESPONSE" >/dev/null; then
    check "Manager can see hidden product" true true
else
    check "Manager can see hidden product" true false
fi

check "Catalog Manager reactivate product" 200 \
    "$(request PUT "$API/$PRODUCT_ID" "$MANAGER_TOKEN" "$BODY")"

check "Public can view reactivated product" 200 \
    "$(request GET "$API/$PRODUCT_ID")"

echo
echo "===== DELETE TEST PRODUCT ====="

STATUS=$(request DELETE "$API/$PRODUCT_ID" "$MANAGER_TOKEN")
check "Catalog Manager delete product" 204 "$STATUS"

check "Deleted product returns 404" 404 \
    "$(request GET "$API/$PRODUCT_ID")"

if [[ "$STATUS" == "204" ]]; then
    PRODUCT_ID=""
fi

echo
echo "========================================"
echo " TEST RESULTS"
echo "========================================"
echo " PASSED: $PASSED"
echo " FAILED: $FAILED"
echo " TOTAL : $((PASSED + FAILED))"
echo "========================================"

if [[ "$FAILED" -gt 0 ]]; then
    exit 1
fi

echo "ALL TESTS PASSED"


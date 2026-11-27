
#!/usr/bin/env bash
set -euo pipefail

API="http://localhost:8000/api"
KEYCLOAK="http://localhost:8081"
REALM="marketplace"
CLIENT_ID="marketplace-web"

PASSED=0
FAILED=0

RESPONSE=$(mktemp)
trap 'rm -f "$RESPONSE"' EXIT

command -v jq >/dev/null || {
    echo "ERROR: jq is required"
    exit 1
}

get_token() {
    local username="$1"
    local password result token

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
        echo "ERROR: Login failed for $username" >&2
        echo "$result" | jq -r \
            '.error_description // .error' >&2
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
        -sS --max-time 20
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
        echo "[PASS] $name ($actual)"
        PASSED=$((PASSED + 1))
    else
        echo "[FAIL] $name: expected $expected, got $actual"
        cat "$RESPONSE"
        echo
        FAILED=$((FAILED + 1))
    fi
}

echo "========================================"
echo " ORDER AUTHORIZATION TEST"
echo "========================================"

CUSTOMER_TOKEN=$(get_token test-user1)

read -rp "Second customer username [test-user2]: " SECOND_USER
SECOND_USER="${SECOND_USER:-test-user2}"

if [[ "$SECOND_USER" == "test-user1" ]]; then
    echo "Please use a different customer account."
    exit 1
fi

SECOND_TOKEN=$(get_token "$SECOND_USER")
MANAGER_TOKEN=$(get_token catalog_manager)
LICENSE_TOKEN=$(get_token license_coordinator)
AUDITOR_TOKEN=$(get_token transaction_auditor)

echo
echo "===== SELECT ACTIVE PRODUCT ====="

PRODUCTS=$(curl -fsS --max-time 15 \
    "$API/catalog/products?pageSize=100")

PRODUCT_ID=$(echo "$PRODUCTS" | jq -r \
    '[.items[] | select(.isActive == true and .price > 0)]
    | .[0].id // empty')

PRODUCT_PRICE=$(echo "$PRODUCTS" | jq -r \
    --arg id "$PRODUCT_ID" \
    '.items[] | select(.id == $id) | .price')

if [[ -z "$PRODUCT_ID" ]]; then
    echo "ERROR: No active product found."
    exit 1
fi

echo "Product ID: $PRODUCT_ID"
echo "Product price: $PRODUCT_PRICE"

BODY=$(jq -n --arg id "$PRODUCT_ID" '{
    productId: $id,
    quantity: 1
}')

UNKNOWN_ID="00000000-0000-0000-0000-000000000000"

echo
echo "===== ANONYMOUS ACCESS ====="

check "Anonymous list orders" 401 \
    "$(request GET "$API/orders")"

check "Anonymous create order" 401 \
    "$(request POST "$API/orders" "" "$BODY")"

check "Anonymous get order" 401 \
    "$(request GET "$API/orders/$UNKNOWN_ID")"

check "Anonymous pay order" 401 \
    "$(request POST "$API/orders/$UNKNOWN_ID/pay")"

echo
echo "===== STAFF PERMISSIONS ====="

for role in catalog_manager license_coordinator transaction_auditor
do
    case "$role" in
        catalog_manager) TOKEN="$MANAGER_TOKEN" ;;
        license_coordinator) TOKEN="$LICENSE_TOKEN" ;;
        transaction_auditor) TOKEN="$AUDITOR_TOKEN" ;;
    esac

    check "$role cannot list orders" 403 \
        "$(request GET "$API/orders" "$TOKEN")"

    check "$role cannot create orders" 403 \
        "$(request POST "$API/orders" "$TOKEN" "$BODY")"

    check "$role cannot get orders" 403 \
        "$(request GET "$API/orders/$UNKNOWN_ID" "$TOKEN")"

    check "$role cannot pay orders" 403 \
        "$(request POST "$API/orders/$UNKNOWN_ID/pay" "$TOKEN")"
done

echo
echo "===== CUSTOMER ACCESS ====="

check "Customer can list orders" 200 \
    "$(request GET "$API/orders" "$CUSTOMER_TOKEN")"

# Send forged price and owner fields.
# The server must use the catalog price and JWT subject.
FORGED_BODY=$(echo "$BODY" | jq \
    --arg user "$SECOND_USER" \
    '. + {
        price: 1,
        totalAmount: 1,
        userId: $user,
        keycloakUserId: $user
    }')

STATUS=$(request POST "$API/orders" \
    "$CUSTOMER_TOKEN" "$FORGED_BODY")

check "Customer create order" 201 "$STATUS"

if [[ "$STATUS" != "201" ]]; then
    echo "Cannot continue without a test order."
    echo "Check VNPay configuration and Catalog Service."
    exit 1
fi

ORDER_ID=$(jq -r '.id // empty' "$RESPONSE")

if [[ -z "$ORDER_ID" ]]; then
    echo "ERROR: Order ID not found"
    exit 1
fi

echo "Test order ID: $ORDER_ID"

if jq -e --argjson price "$PRODUCT_PRICE" \
    '.totalAmount == $price and
    (.items | length == 1) and
    .items[0].price == $price' \
    "$RESPONSE" >/dev/null; then

    check "Server uses real catalog price" true true
else
    check "Server uses real catalog price" true false
fi

if jq -e \
    '.paymentUrl | type == "string" and length > 0' \
    "$RESPONSE" >/dev/null; then

    check "Order has payment URL" true true
else
    check "Order has payment URL" true false
fi

check "Customer can view own order" 200 \
    "$(request GET "$API/orders/$ORDER_ID" "$CUSTOMER_TOKEN")"

check "Customer can list own orders" 200 \
    "$(request GET "$API/orders" "$CUSTOMER_TOKEN")"

if jq -e --arg id "$ORDER_ID" \
    'any(.[]; .id == $id)' \
    "$RESPONSE" >/dev/null; then

    check "Own order appears in own list" true true
else
    check "Own order appears in own list" true false
fi

echo
echo "===== OWNERSHIP SECURITY ====="

check "Second customer can list own orders" 200 \
    "$(request GET "$API/orders" "$SECOND_TOKEN")"

if jq -e --arg id "$ORDER_ID" \
    'type == "array" and (any(.[]; .id == $id) | not)' \
    "$RESPONSE" >/dev/null; then

    check "Other customer cannot list test order" true true
else
    check "Other customer cannot list test order" true false
fi

check "Other customer cannot read test order" 404 \
    "$(request GET "$API/orders/$ORDER_ID" "$SECOND_TOKEN")"

check "Other customer cannot pay test order" 404 \
    "$(request POST "$API/orders/$ORDER_ID/pay" "$SECOND_TOKEN")"

check "Owner can generate payment URL again" 200 \
    "$(request POST "$API/orders/$ORDER_ID/pay" "$CUSTOMER_TOKEN")"

echo
echo "===== INPUT VALIDATION ====="

INVALID_BODY=$(echo "$BODY" | jq '.quantity = 0')

check "Reject zero quantity" 400 \
    "$(request POST "$API/orders" "$CUSTOMER_TOKEN" "$INVALID_BODY")"

INVALID_BODY=$(echo "$BODY" | jq '.quantity = 11')

check "Reject quantity greater than 10" 400 \
    "$(request POST "$API/orders" "$CUSTOMER_TOKEN" "$INVALID_BODY")"

INVALID_BODY=$(jq -n --arg id "$UNKNOWN_ID" '{
    productId: $id,
    quantity: 1
}')

check "Reject nonexistent product" 400 \
    "$(request POST "$API/orders" "$CUSTOMER_TOKEN" "$INVALID_BODY")"

echo
echo "========================================"
echo " TEST RESULTS"
echo "========================================"
echo " PASSED: $PASSED"
echo " FAILED: $FAILED"
echo " TOTAL : $((PASSED + FAILED))"
echo "========================================"
echo " Test order retained: $ORDER_ID"

if [[ "$FAILED" -gt 0 ]]; then
    exit 1
fi

echo "ALL TESTS PASSED"

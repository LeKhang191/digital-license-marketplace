#!/usr/bin/env bash
set -euo pipefail
# Credentials are supplied at runtime; never embed demo passwords in Git.
: "${TEST_ADMIN_USER:?Set TEST_ADMIN_USER}"
: "${TEST_ADMIN_PASSWORD:?Set TEST_ADMIN_PASSWORD}"
: "${TEST_CUSTOMER_USER:?Set TEST_CUSTOMER_USER}"
: "${TEST_CUSTOMER_PASSWORD:?Set TEST_CUSTOMER_PASSWORD}"
KC=http://localhost:8081/realms/marketplace/protocol/openid-connect/token
GW=http://localhost:8000/api/catalog/products

token() {
  curl -s -X POST "$KC" \
    -d "client_id=marketplace-web&grant_type=password" \
    --data-urlencode "username=$1" --data-urlencode "password=$2" |
    sed -E 's/.*"access_token":"([^"]+)".*/\1/'
}
body() { echo "{\"name\":\"Test $RANDOM\",\"slug\":\"t-$RANDOM-$RANDOM\",\"category\":\"Game\",\"price\":10000}"; }
code() { curl -s -o /dev/null -w "%{http_code}" "$@"; }

ADMIN=$(token "$TEST_ADMIN_USER" "$TEST_ADMIN_PASSWORD")
CUST=$(token "$TEST_CUSTOMER_USER" "$TEST_CUSTOMER_PASSWORD")
[ ${#ADMIN} -gt 100 ] || { echo "Failed to get admin token. Check TEST_ADMIN_USER/TEST_ADMIN_PASSWORD and Keycloak."; exit 1; }
[ ${#CUST} -gt 100 ] || { echo "Failed to get customer token. Check TEST_CUSTOMER_USER/TEST_CUSTOMER_PASSWORD and Keycloak."; exit 1; }

echo "GET public (at 200):           $(code $GW)"
echo "POST no token (at 401):        $(code -X POST $GW -H 'Content-Type: application/json' -d "$(body)")"
echo "POST token customer (at 403):     $(code -X POST $GW -H "Authorization: Bearer $CUST" -H 'Content-Type: application/json' -d "$(body)")"
echo "POST token admin (at 201):        $(code -X POST $GW -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d "$(body)")"

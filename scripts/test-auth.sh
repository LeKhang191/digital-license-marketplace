#!/usr/bin/env bash
set -u
KC=http://localhost:8081/realms/marketplace/protocol/openid-connect/token
GW=http://localhost:8000/api/catalog/products

token() {
  curl -s -X POST "$KC" \
    -d "client_id=marketplace-web&grant_type=password" \
    -d "username=$1&password=$2" |
    sed -E 's/.*"access_token":"([^"]+)".*/\1/'
}
body() { echo "{\"name\":\"Test $RANDOM\",\"slug\":\"t-$RANDOM-$RANDOM\",\"category\":\"Game\",\"price\":10}"; }
code() { curl -s -o /dev/null -w "%{http_code}" "$@"; }

ADMIN=$(token admin1 'Admin@123')
CUST=$(token customer1 'Customer@123')
[ ${#ADMIN} -gt 100 ] || { echo "Failed to get admin token. Check Keycloak."; exit 1; }

echo "GET public (at 200):           $(code $GW)"
echo "POST no token (at 401):        $(code -X POST $GW -H 'Content-Type: application/json' -d "$(body)")"
echo "POST token customer (at 403):     $(code -X POST $GW -H "Authorization: Bearer $CUST" -H 'Content-Type: application/json' -d "$(body)")"
echo "POST token admin (at 201):        $(code -X POST $GW -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d "$(body)")"

#!/usr/bin/env bash
set -u
KC=http://localhost:8081/realms/marketplace/protocol/openid-connect/token
GW=http://localhost:8000/api
token() {
  curl -s -X POST "$KC" -d "client_id=marketplace-web&grant_type=password" \
    -d "username=$1&password=$2" | sed -E 's/.*"access_token":"([^"]+)".*/\1/'
}
first_id() { grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4; }
json="Content-Type: application/json"

ADMIN=$(token admin1 'Admin@123'); CUST=$(token customer1 'Customer@123')
[ ${#ADMIN} -gt 100 ] || { echo "Không lấy được token. Kiểm tra Keycloak."; exit 1; }

PID=$(curl -s -X POST $GW/catalog/products -H "Authorization: Bearer $ADMIN" -H "$json" \
  -d "{\"name\":\"Order Test $RANDOM\",\"slug\":\"o-$RANDOM-$RANDOM\",\"category\":\"Game\",\"price\":25.5}" | first_id)
echo "Sản phẩm test: $PID"

code() { curl -s -o /dev/null -w "%{http_code}" "$@"; }
echo "POST single no token (401):        $(code -X POST $GW/orders -H "$json" -d "{\"productId\":\"$PID\",\"quantity\":1}")"
echo "POST single with invalid product (400):        $(code -X POST $GW/orders -H "Authorization: Bearer $CUST" -H "$json" -d '{"productId":"00000000-0000-0000-0000-000000000001","quantity":1}')"

RES=$(curl -s -X POST $GW/orders -H "Authorization: Bearer $CUST" -H "$json" -d "{\"productId\":\"$PID\",\"quantity\":2}")
OID=$(echo "$RES" | first_id)
echo "POST single with 2 items (at 51, PENDING):"; echo "  $RES"
echo "GET /orders of customer (200):    $(code $GW/orders -H "Authorization: Bearer $CUST")"
echo "GET single of urself (200):      $(code $GW/orders/$OID -H "Authorization: Bearer $CUST")"
echo "GET single from others - admin (404):  $(code $GW/orders/$OID -H "Authorization: Bearer $ADMIN")"

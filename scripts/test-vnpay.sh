#!/usr/bin/env bash
# Mô phỏng VNPay gọi IPN (có ký HMAC-SHA512 bằng HashSecret trong .env)
# và kiểm tra: trạng thái đơn, chống gọi trùng, chữ ký sai, sự kiện RabbitMQ.
set -u
cd "$(dirname "$0")/.."

envget() { grep -E "^$1=" .env | head -1 | cut -d= -f2-; }
SECRET=$(envget VNPAY_HASH_SECRET); TMN=$(envget VNPAY_TMN_CODE)
RU=$(envget RABBITMQ_USER); RP=$(envget RABBITMQ_PASSWORD)
[ -n "$SECRET" ] && [ -n "$TMN" ] || { echo "Chưa điền VNPAY_TMN_CODE / VNPAY_HASH_SECRET trong .env (rồi docker compose up -d)"; exit 1; }

KC=http://localhost:8081/realms/marketplace/protocol/openid-connect/token
GW=http://localhost:8000/api
MG=http://localhost:15672/api
json="Content-Type: application/json"
token() { curl -s -X POST "$KC" -d "client_id=marketplace-web&grant_type=password" -d "username=$1&password=$2" | sed -E 's/.*"access_token":"([^"]+)".*/\1/'; }
first_id() { grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4; }

ADMIN=$(token admin1 'Admin@123'); CUST=$(token customer1 'Customer@123')
[ ${#ADMIN} -gt 100 ] || { echo "Không lấy được token. Kiểm tra Keycloak."; exit 1; }

# Hàng đợi tạm để đọc sự kiện order.paid
mg() { curl -s -u "$RU:$RP" -H "$json" "$@"; }
mg -X PUT $MG/exchanges/%2F/marketplace.events -d '{"type":"topic","durable":true}' >/dev/null
mg -X PUT $MG/queues/%2F/test.order.paid -d '{"durable":false}' >/dev/null
mg -X DELETE $MG/queues/%2F/test.order.paid/contents >/dev/null
mg -X POST $MG/bindings/%2F/e/marketplace.events/q/test.order.paid -d '{"routing_key":"order.paid"}' >/dev/null

# Sản phẩm 250000 VND + đơn của customer
PID=$(curl -s -X POST $GW/catalog/products -H "Authorization: Bearer $ADMIN" -H "$json" \
  -d "{\"name\":\"Pay Test $RANDOM\",\"slug\":\"p-$RANDOM-$RANDOM\",\"category\":\"Game\",\"price\":250000}" | first_id)
RES=$(curl -s -X POST $GW/orders -H "Authorization: Bearer $CUST" -H "$json" -d "{\"productId\":\"$PID\",\"quantity\":1}")
OID=$(echo "$RES" | first_id)
URL=$(echo "$RES" | grep -o '"paymentUrl":"[^"]*"' | cut -d'"' -f4 | sed 's/\\u0026/\&/g')
[ -n "$URL" ] || { echo "Không có paymentUrl. Phản hồi: $RES"; exit 1; }
REF=$(echo "$URL" | grep -o 'vnp_TxnRef=[^&]*' | cut -d= -f2)
echo "Đơn: $OID"
echo "Link thanh toán (mở bằng trình duyệt để thử thanh toán thật ở sandbox):"
echo "  $URL"
echo

# Dựng IPN giả có chữ ký (các khóa đã xếp theo thứ tự chữ cái)
AMOUNT=25000000
mkqs() { echo "vnp_Amount=$1&vnp_BankCode=NCB&vnp_CardType=ATM&vnp_OrderInfo=ThanhToanDonHang&vnp_PayDate=20261122120000&vnp_ResponseCode=$2&vnp_TmnCode=$TMN&vnp_TransactionNo=14123456&vnp_TransactionStatus=$2&vnp_TxnRef=$REF"; }
sign() { printf %s "$1" | openssl dgst -sha512 -hmac "$SECRET" | awk '{print $NF}'; }
ipn() { curl -s "$GW/orders/vnpay-ipn?$1&vnp_SecureHash=$2"; }

QS=$(mkqs $AMOUNT 00); H=$(sign "$QS")
BAD=$(mkqs 1 00)
echo "IPN sai chữ ký        (mong đợi 97): $(ipn "$BAD" "$H")"
echo "IPN đúng              (mong đợi 00): $(ipn "$QS" "$H")"
echo "IPN gọi lại           (mong đợi 02): $(ipn "$QS" "$H")"
echo "Trạng thái đơn        (mong đợi PAID): $(curl -s $GW/orders/$OID -H "Authorization: Bearer $CUST" | grep -o '"status":"[^"]*"')"
echo
echo "Sự kiện order.paid trong RabbitMQ (mong đợi 1 tin nhắn):"
mg -X POST $MG/queues/%2F/test.order.paid/get -d '{"count":5,"ackmode":"ack_requeue_false","encoding":"auto"}' | grep -o '"payload":"[^}]*' | head -3

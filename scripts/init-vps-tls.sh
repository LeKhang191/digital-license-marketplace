#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

# Cấp chứng chỉ Let's Encrypt lần đầu cho APP_DOMAIN và AUTH_DOMAIN (không dùng Cloudflare).
# Điều kiện tiên quyết:
#   1. DNS A/AAAA của APP_DOMAIN và AUTH_DOMAIN đã trỏ đúng IP public của server.
#   2. Firewall/VPS mở cổng 80 và 443.
#   3. File .env có APP_DOMAIN, AUTH_DOMAIN, CERTBOT_EMAIL (hoặc truyền qua env).

if [ -f .env ]; then
    set -a
    # shellcheck disable=SC1091
    source .env
    set +a
fi

: "${APP_DOMAIN:?Missing APP_DOMAIN (e.g. marketplace.example.com)}"
: "${AUTH_DOMAIN:?Missing AUTH_DOMAIN (e.g. auth.example.com)}"
: "${CERTBOT_EMAIL:?Missing CERTBOT_EMAIL (e.g. admin@example.com)}"

COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.vps.yml)
STAGING_FLAG=()
if [ "${CERTBOT_STAGING:-false}" = "true" ]; then
    STAGING_FLAG=(--staging)
    echo "[INFO] Running Certbot in Let's Encrypt STAGING mode"
fi

echo "=== [1/3] Starting Nginx in HTTP bootstrap mode (ACME HTTP-01) ==="
NGINX_MODE=bootstrap-http "${COMPOSE[@]}" up -d --no-deps --force-recreate nginx

echo "=== [2/3] Requesting Let's Encrypt certificate for ${APP_DOMAIN} and ${AUTH_DOMAIN} ==="
"${COMPOSE[@]}" --profile certbot run --rm certbot certonly \
    --webroot \
    -w /var/www/certbot \
    --email "${CERTBOT_EMAIL}" \
    --agree-tos \
    --no-eff-email \
    --cert-name "${APP_DOMAIN}" \
    "${STAGING_FLAG[@]}" \
    -d "${APP_DOMAIN}" \
    -d "${AUTH_DOMAIN}"

echo "=== [3/3] Switching Nginx to HTTPS mode and verifying configuration ==="
NGINX_MODE=site-https "${COMPOSE[@]}" up -d --no-deps --force-recreate nginx
"${COMPOSE[@]}" exec -T nginx nginx -t

echo ""
echo "[DONE] HTTPS enabled for:"
echo "  Frontend & API : https://${APP_DOMAIN}"
echo "  VNPay IPN      : https://${APP_DOMAIN}/api/orders/vnpay-ipn"
echo "  Jira Webhook   : https://${APP_DOMAIN}/api/support/webhooks/jira"
echo "  Keycloak Auth  : https://${AUTH_DOMAIN}/realms/marketplace"
echo "  Google Redirect: https://${AUTH_DOMAIN}/realms/marketplace/broker/google/endpoint"
echo ""
echo "Gia hạn định kỳ (cron): docker compose -f docker-compose.yml -f docker-compose.vps.yml --profile certbot run --rm certbot renew && docker compose -f docker-compose.yml -f docker-compose.vps.yml exec -T nginx nginx -s reload"

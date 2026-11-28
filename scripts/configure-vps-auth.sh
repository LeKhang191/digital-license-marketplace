#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

# Cập nhật Keycloak clients (marketplace-web, account-console) cho domain VPS HTTPS
# mà không cần bật chế độ Codespaces. Chạy sau khi đã chạy bash scripts/configure-auth.sh.

if [ -f .env ]; then
    set -a
    # shellcheck disable=SC1091
    source .env
    set +a
fi

: "${APP_DOMAIN:=${PUBLIC_HOST:-}}"
: "${AUTH_DOMAIN:=${PUBLIC_HOST:-}}"
: "${APP_DOMAIN:?Missing APP_DOMAIN or PUBLIC_HOST}"
: "${AUTH_DOMAIN:?Missing AUTH_DOMAIN or PUBLIC_HOST}"

REALM="marketplace"

python3 - "${APP_DOMAIN}" "${AUTH_DOMAIN}" "${REALM}" <<'PY'
import json
import os
import subprocess
import sys

app_domain, auth_domain, realm = sys.argv[1], sys.argv[2], sys.argv[3]
app_url = f"https://{app_domain}"
auth_url = f"https://{auth_domain}"
kcadm = "/opt/keycloak/bin/kcadm.sh"

kc_pass = os.environ.get("KC_PASSWORD", "")
if kc_pass:
    subprocess.run(
        [
            "docker", "compose", "exec", "-T",
            "-e", f"KC_ADMIN_PASS={kc_pass}",
            "keycloak", "sh", "-c",
            f'{kcadm} config credentials --server http://localhost:8080 --realm master --user admin --password "$KC_ADMIN_PASS"',
        ],
        check=True,
        stdout=subprocess.DEVNULL,
    )


def kc(*args, data=None):
    cmd = [
        "docker", "compose", "exec", "-T", "keycloak",
        kcadm, *args,
    ]
    return subprocess.check_output(
        cmd,
        input=json.dumps(data) if data is not None else None,
        text=True,
    )


def update_client(client_id, fields):
    clients = json.loads(kc(
        "get", "clients",
        "-r", realm,
        "-q", f"clientId={client_id}",
        "--fields", "id,clientId",
    ))
    matches = [c for c in clients if c.get("clientId") == client_id]
    if len(matches) != 1:
        raise SystemExit(f"Expected 1 client for {client_id}, found {len(matches)}")
    kc("update", f"clients/{matches[0]['id']}", "-r", realm, "-f", "-", data=fields)


update_client("account-console", {
    "webOrigins": [
        auth_url,
        "http://localhost:8081",
    ],
})

update_client("marketplace-web", {
    "standardFlowEnabled": True,
    "directAccessGrantsEnabled": True,
    "redirectUris": [
        f"{app_url}/*",
        "http://localhost:5173/*",
    ],
    "webOrigins": [
        app_url,
        "http://localhost:5173",
    ],
})

print("[PASS] Keycloak public clients updated")
print(f"Frontend URL       : {app_url}")
print(f"Keycloak Issuer    : {auth_url}/realms/{realm}")
print(f"Google Redirect URI: {auth_url}/realms/{realm}/broker/google/endpoint")
print(f"Jira Webhook URL   : {app_url}/api/support/webhooks/jira")
print(f"VNPay IPN URL      : {app_url}/api/orders/vnpay-ipn")
PY

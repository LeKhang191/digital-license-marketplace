#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

# Tạo hoặc cập nhật B2B Partner Confidential Client trên Keycloak (Phương án A: OAuth2 client_credentials)
# - Gán realm role: partner_api (gỡ default-roles-marketplace / customer để cô lập quyền tuyệt đối)
# - Gắn Hardcoded Claim Protocol Mapper: partner_id -> Access Token
#
# Cách dùng:
#   bash scripts/provision-partner.sh prt_khen_demo [client_id]

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
    echo "Usage: bash scripts/provision-partner.sh <partner_id> [client_id]" >&2
    echo "Example: bash scripts/provision-partner.sh prt_khen_demo" >&2
    exit 1
fi

PARTNER_ID="$1"
CLIENT_ID="${2:-partner-${PARTNER_ID}}"
REALM="marketplace"

if ! [[ "${PARTNER_ID}" =~ ^[a-z0-9][a-z0-9_-]{2,47}$ ]]; then
    echo "[ERROR] Invalid partner_id '${PARTNER_ID}'. Must be 3..48 chars matching ^[a-z0-9][a-z0-9_-]{2,47}$" >&2
    exit 1
fi

if [ -f .env ]; then
    set -a
    # shellcheck disable=SC1091
    source .env
    set +a
fi

if [[ -z "$(docker compose ps -q keycloak)" ]]; then
    echo "[ERROR] Keycloak container is not running." >&2
    exit 1
fi

KC_PUBLIC_URL="${KEYCLOAK_PUBLIC_URL:-http://localhost:8081}"

python3 - "${PARTNER_ID}" "${CLIENT_ID}" "${REALM}" "${KC_PUBLIC_URL}" <<'PY'
import json
import os
import subprocess
import sys

partner_id, client_id, realm, kc_public_url = sys.argv[1:5]
kcadm = "/opt/keycloak/bin/kcadm.sh"


def kc(*args, data=None, check=True):
    cmd = ["docker", "compose", "exec", "-T", "keycloak", kcadm, *args]
    res = subprocess.run(
        cmd,
        input=json.dumps(data) if data is not None else None,
        text=True,
        capture_output=True,
    )
    if check and res.returncode != 0:
        raise RuntimeError(f"kcadm failed ({args[0]}): {res.stderr.strip()}")
    return res


kc_pass = os.environ.get("KC_PASSWORD", "")
if kc_pass:
    auth = subprocess.run(
        [
            "docker", "compose", "exec", "-T",
            "-e", f"KC_ADMIN_PASS={kc_pass}",
            "keycloak", "sh", "-c",
            f'{kcadm} config credentials --server http://localhost:8080 --realm master --user admin --password "$KC_ADMIN_PASS"',
        ],
        text=True,
        capture_output=True,
    )
    if auth.returncode != 0:
        raise RuntimeError(f"Keycloak admin login failed: {auth.stderr.strip()}")
else:
    subprocess.check_call([
        "docker", "compose", "exec", "keycloak", kcadm,
        "config", "credentials",
        "--server", "http://localhost:8080",
        "--realm", "master",
        "--user", "admin",
    ])

# 1. Ensure partner_api role exists
role_check = kc("get", "roles/partner_api", "-r", realm, check=False)
if role_check.returncode != 0:
    kc(
        "create", "roles", "-r", realm,
        "-s", "name=partner_api",
        "-s", "description=B2B Partner API client",
    )
    print("[OK] Created realm role: partner_api")

# 2. Create or update confidential client with service accounts enabled
clients = json.loads(kc("get", "clients", "-r", realm, "-q", f"clientId={client_id}").stdout)
matches = [c for c in clients if c.get("clientId") == client_id]

client_payload = {
    "clientId": client_id,
    "name": f"Partner API ({partner_id})",
    "enabled": True,
    "protocol": "openid-connect",
    "publicClient": False,
    "standardFlowEnabled": False,
    "implicitFlowEnabled": False,
    "directAccessGrantsEnabled": False,
    "serviceAccountsEnabled": True,
    "fullScopeAllowed": True,
}

if matches:
    client_uuid = matches[0]["id"]
    kc("update", f"clients/{client_uuid}", "-r", realm, "-f", "-", data=client_payload)
    print(f"[OK] Updated confidential client: {client_id}")
else:
    kc("create", "clients", "-r", realm, "-f", "-", data=client_payload)
    clients = json.loads(kc("get", "clients", "-r", realm, "-q", f"clientId={client_id}").stdout)
    matches = [c for c in clients if c.get("clientId") == client_id]
    assert len(matches) == 1, f"Failed to locate created client {client_id}"
    client_uuid = matches[0]["id"]
    print(f"[OK] Created confidential client: {client_id}")

# 3. Ensure hardcoded partner_id claim protocol mapper
mappers = json.loads(kc("get", f"clients/{client_uuid}/protocol-mappers/models", "-r", realm).stdout)
existing_mapper = next((m for m in mappers if m.get("name") == "partner-id-mapper"), None)

mapper_payload = {
    "name": "partner-id-mapper",
    "protocol": "openid-connect",
    "protocolMapper": "oidc-hardcoded-claim-mapper",
    "consentRequired": False,
    "config": {
        "claim.name": "partner_id",
        "claim.value": partner_id,
        "jsonType.label": "String",
        "access.token.claim": "true",
        "id.token.claim": "false",
        "userinfo.token.claim": "false",
    },
}

if existing_mapper:
    mapper_payload["id"] = existing_mapper["id"]
    kc(
        "update",
        f"clients/{client_uuid}/protocol-mappers/models/{existing_mapper['id']}",
        "-r", realm, "-f", "-", data=mapper_payload,
    )
    print(f"[OK] Updated partner_id claim mapper -> {partner_id}")
else:
    kc("create", f"clients/{client_uuid}/protocol-mappers/models", "-r", realm, "-f", "-", data=mapper_payload)
    print(f"[OK] Created partner_id claim mapper -> {partner_id}")

# 4. Harden service account roles: remove default customer/staff roles and assign ONLY partner_api
sa_user = json.loads(kc("get", f"clients/{client_uuid}/service-account-user", "-r", realm).stdout)
sa_username = sa_user["username"]

for forbidden_role in [
    f"default-roles-{realm}",
    "customer",
    "catalog_manager",
    "license_coordinator",
    "transaction_auditor",
    "admin",
]:
    kc("remove-roles", "-r", realm, "--uusername", sa_username, "--rolename", forbidden_role, check=False)

kc("add-roles", "-r", realm, "--uusername", sa_username, "--rolename", "partner_api")
print(f"[OK] Assigned ONLY partner_api role to service account: {sa_username}")

# 5. Fetch client secret
secret_info = json.loads(kc("get", f"clients/{client_uuid}/client-secret", "-r", realm).stdout)
client_secret = secret_info["value"]

print("")
print("=========================================")
print(" Partner API Client Ready (Method A)")
print("=========================================")
print(f"Partner ID    : {partner_id}")
print(f"Client ID     : {client_id}")
print(f"Client Secret : {client_secret}")
print(f"Token URL     : {kc_public_url.rstrip('/')}/realms/{realm}/protocol/openid-connect/token")
print("")
print("NOTE: Keep Client Secret private. Never commit it to Git.")
PY

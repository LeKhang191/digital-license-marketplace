#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."

if [[ "${CODESPACES:-false}" != "true" ]]; then
    echo "[STOP] This script is for GitHub Codespaces only."
    exit 1
fi

: "${CODESPACE_NAME:?Missing CODESPACE_NAME}"
: "${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN:?Missing forwarding domain}"

python3 <<'PY'
import os
import re
from pathlib import Path

name = os.environ["CODESPACE_NAME"]
domain = os.environ["GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN"]

host = f"{name}-8081.{domain}"

assert re.fullmatch(r"[a-zA-Z0-9.-]+", host)

settings = {
    "KEYCLOAK_PUBLIC_HOST": host,
    "KEYCLOAK_PUBLIC_URL": f"https://{host}",
    "FRONTEND_PUBLIC_URL": f"https://{name}-5173.{domain}",
    "COMPOSE_FILE": "docker-compose.yml:docker-compose.auth-proxy.yml"
}

path = Path(".env")
assert path.exists(), ".env must exist"

content = path.read_text()

for key, value in settings.items():
    pattern = rf"^{re.escape(key)}=.*$"
    content = re.sub(pattern, "", content, flags=re.M)
    content = content.rstrip() + f"\n{key}={value}\n"

path.write_text(content)
path.chmod(0o600)

print("[PASS] Codespaces authentication configured")
print("Keycloak:", settings["KEYCLOAK_PUBLIC_URL"])
print("Frontend:", settings["FRONTEND_PUBLIC_URL"])
PY


#!/usr/bin/env bash

set -Eeuo pipefail
cd "$(dirname "$0")/.."

# Only run inside GitHub Codespaces.
if [[ "${CODESPACES:-false}" != "true" ]]; then
    echo "[Skip] Not running in GitHub Codespaces."
    exit 0
fi

if ! command -v iptables-legacy >/dev/null 2>&1; then
    echo "[Skip] iptables-legacy is not available."
    exit 0
fi

POSTGRES_ID=$(docker compose ps -q postgres)

if [[ -z "$POSTGRES_ID" ]]; then
    echo "[ERROR] Start PostgreSQL first."
    exit 1
fi

PROJECT=$(docker inspect \
    --format '{{index .Config.Labels "com.docker.compose.project"}}' \
    "$POSTGRES_ID")

NETWORK="${PROJECT}_backend"

NETWORK_ID=$(docker network inspect \
    --format '{{.Id}}' "$NETWORK")

BRIDGE="br-${NETWORK_ID:0:12}"

echo "[Network] $NETWORK"
echo "[Bridge] $BRIDGE"

if ! ip link show "$BRIDGE" >/dev/null 2>&1; then
    echo "[ERROR] Docker bridge not found."
    exit 1
fi

if sudo iptables-legacy -C FORWARD \
    -i "$BRIDGE" -o "$BRIDGE" -j ACCEPT 2>/dev/null; then

    echo "[OK] Firewall rule already exists."

else
    sudo iptables-legacy -I FORWARD 1 \
        -i "$BRIDGE" -o "$BRIDGE" -j ACCEPT

    echo "[OK] Docker bridge firewall rule added."
fi

echo "[Test] Checking PostgreSQL connectivity..."

docker run --rm \
    --network "$NETWORK" \
    postgres:16 \
    pg_isready -h postgres -p 5432 \
    -U app -d catalog_db -t 5

echo "[OK] Network connectivity check completed."

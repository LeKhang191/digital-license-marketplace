
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


echo "[Test] Checking outbound firewall..."

OUT_IF=$(ip -4 route show default |
    awk 'NR==1 {print $5}')

if [[ -z "$OUT_IF" ]]; then
    echo "[ERROR] Default network interface not found."
    exit 1
fi

ensure_forward_rule() {
    if ! sudo iptables-legacy -C FORWARD "$@" 2>/dev/null; then
        sudo iptables-legacy -I FORWARD 1 "$@"
    fi
}

# Allow outbound connections from the project bridge.
ensure_forward_rule \
    -i "$BRIDGE" -o "$OUT_IF" \
    -m conntrack \
    --ctstate NEW,ESTABLISHED,RELATED \
    -j ACCEPT

# Allow responses to existing outbound connections.
ensure_forward_rule \
    -i "$OUT_IF" -o "$BRIDGE" \
    -m conntrack \
    --ctstate ESTABLISHED,RELATED \
    -j ACCEPT

echo "[OK] Outbound firewall rules configured."

echo "[Test] Checking PostgreSQL connectivity..."

docker run --rm \
    --network "$NETWORK" \
    postgres:16 \
    pg_isready -h postgres -p 5432 \
    -U app -d catalog_db -t 5

echo "[OK] Network connectivity check completed."

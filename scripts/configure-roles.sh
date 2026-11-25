
#!/usr/bin/env bash

set -Eeuo pipefail

cd "$(dirname "$0")/.."

REALM="marketplace"
KCADM="/opt/keycloak/bin/kcadm.sh"

kc() {
    docker compose exec -T keycloak "$KCADM" "$@"
}

echo "================================="
echo " Marketplace Business Roles"
echo "================================="

if [[ -z "$(docker compose ps -q keycloak)" ]]; then
    echo "[ERROR] Keycloak is not running."
    exit 1
fi

echo "[1/4] Authenticating Keycloak administrator..."

docker compose exec keycloak "$KCADM" \
    config credentials \
    --server http://localhost:8080 \
    --realm master \
    --user admin

ensure_role() {
    local name="$1"
    local description="$2"

    if kc get "roles/$name" -r "$REALM" \
        >/dev/null 2>&1; then

        echo "[SKIP] Role already exists: $name"

    else

        kc create roles \
            -r "$REALM" \
            -s "name=$name" \
            -s "description=$description"

        echo "[OK] Created role: $name"
    fi
}

echo "[2/4] Creating business roles..."

ensure_role \
    customer \
    "Marketplace customer"

ensure_role \
    catalog_manager \
    "Product catalog manager"

ensure_role \
    license_coordinator \
    "License inventory coordinator"

ensure_role \
    transaction_auditor \
    "Transaction and refund auditor"

echo "[3/4] Configuring default customer role..."

DEFAULT_ROLE="default-roles-${REALM}"

kc get "roles/$DEFAULT_ROLE" \
    -r "$REALM" > /dev/null
    
echo "[Default role] $DEFAULT_ROLE"

if ! kc get \
    "roles/$DEFAULT_ROLE/composites" \
    -r "$REALM" | \
    python3 -c '
import json
import sys

roles = json.load(sys.stdin)
sys.exit(0 if any(
    role["name"] == "customer"
    for role in roles
) else 1)
'; then

    kc add-roles \
        -r "$REALM" \
        --rname "$DEFAULT_ROLE" \
        --rolename customer

    echo "[OK] Customer added to default roles."
else
    echo "[SKIP] Customer is already a default role."
fi

echo "[4/4] Updating realm security settings..."

kc update "realms/$REALM" \
    -s duplicateEmailsAllowed=false \
    -s bruteForceProtected=true

echo ""
echo "================================="
echo " Current Realm Roles"
echo "================================="

kc get roles \
    -r "$REALM" \
    --fields name,description

echo ""
echo "[OK] Business role configuration completed."

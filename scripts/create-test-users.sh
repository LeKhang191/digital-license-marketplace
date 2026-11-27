
#!/usr/bin/env bash

set -Eeuo pipefail

cd "$(dirname "$0")/.."

REALM="marketplace"
KCADM="/opt/keycloak/bin/kcadm.sh"

kc() {
    docker compose exec -T keycloak "$KCADM" "$@"
}

echo "================================="
echo " Create Business Test Users"
echo "================================="

if [[ "${CODESPACES:-false}" != "true" ]]; then
    echo "[WARNING] This script is for development."
    read -rp "Continue? [y/N]: " answer

    [[ "$answer" == "y" || "$answer" == "Y" ]] \
        || exit 0
fi

create_user() {
    local username="$1"
    local role="$2"
    local first_name="$3"
    local last_name="$4"
    local password

    local existing

    existing=$(kc get users \
        -r "$REALM" \
        -q "username=$username" \
        -q exact=true \
        --fields username | \
        python3 -c '
import json
import sys

print(len(json.load(sys.stdin)))
')

    if [[ "$existing" != "0" ]]; then
        echo "[SKIP] User already exists: $username"
        return
    fi

    echo ""
    echo "Creating user: $username"
    echo "Business role: $role"

    read -rsp "Enter password: " password
    echo ""

    if [[ -z "$password" ]]; then
        echo "[ERROR] Password cannot be empty."
        return 1
    fi

    kc create users \
        -r "$REALM" \
        -s "username=$username" \
        -s "email=$username@example.com" \
        -s "firstName=$first_name" \
        -s "lastName=$last_name" \
        -s enabled=true \
        -s emailVerified=true

    kc set-password \
        -r "$REALM" \
        --username "$username" \
        --new-password "$password"

    unset password

    kc add-roles \
        -r "$REALM" \
        --uusername "$username" \
        --rolename "$role"

    if [[ "$role" != "customer" ]]; then
        kc add-roles \
            -r "$REALM" \
            --uusername "$username" \
            --rolename offline_access \
            --rolename uma_authorization

        kc add-roles \
            -r "$REALM" \
            --uusername "$username" \
            --cclientid account \
            --rolename manage-account \
            --rolename view-profile

        kc remove-roles \
            -r "$REALM" \
            --uusername "$username" \
            --rolename "default-roles-${REALM}"

        echo "[OK] Staff separated from customer default role"
    fi

    echo "[OK] User created: $username"
}

echo "Authenticating administrator..."

docker compose exec keycloak "$KCADM" \
    config credentials \
    --server http://localhost:8080 \
    --realm master \
    --user admin

create_user \
    test-user1 \
    customer \
    "Test" \
    "Customer"

create_user \
    test-user2 \
    customer \
    "Test" \
    "Customer"

create_user \
    catalog_manager \
    catalog_manager \
    "Catalog" \
    "Manager"

create_user \
    license_coordinator \
    license_coordinator \
    "License" \
    "Coordinator"

create_user \
    transaction_auditor \
    transaction_auditor \
    "Transaction" \
    "Auditor"

echo ""
echo "================================="
echo " Marketplace Test Users"
echo "================================="

kc get users \
    -r "$REALM" \
    --fields username,email,enabled

echo ""
echo "[OK] Test user setup completed."

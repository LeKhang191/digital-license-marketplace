#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."

REALM=marketplace

echo "===== Marketplace Authentication ====="

bash scripts/configure-roles.sh

python3 <<'PY'
import subprocess
import json
import getpass
import os

realm = "marketplace"

def kc(*args, data=None):
    cmd = [
        "docker", "compose", "exec", "-T", "keycloak",
        "/opt/keycloak/bin/kcadm.sh", *args
    ]

    return subprocess.check_output(
        cmd,
        input=json.dumps(data) if data is not None else None,
        text=True
    )

def ask(prompt):
    with open("/dev/tty", "r") as tty:
        print(prompt, end="", flush=True)
        return tty.readline().strip()

kc(
    "update", f"realms/{realm}",
    "-s", "registrationAllowed=true",
    "-s", "resetPasswordAllowed=true",
    "-s", "loginWithEmailAllowed=true",
    "-s", "duplicateEmailsAllowed=false",
    "-s", "rememberMe=true",
    "-s", "bruteForceProtected=true"
)

print("[PASS] Registration and password recovery configured")

if ask("Configure Gmail SMTP? [y/N]: ").lower() == "y":
    email = ask("SMTP Gmail: ")
    password = getpass.getpass("Gmail App Password: ").replace(" ", "")

    assert email and password

    kc(
        "update", f"realms/{realm}", "-f", "-",
        data={
            "smtpServer": {
                "host": "smtp.gmail.com",
                "port": "587",
                "from": email,
                "fromDisplayName": "Digital License Marketplace",
                "auth": "true",
                "ssl": "false",
                "starttls": "true",
                "user": email,
                "password": password
            }
        }
    )

    print("[PASS] SMTP configured")
    print("Test email delivery before enabling mandatory verification")

if ask("Configure Google OAuth? [y/N]: ").lower() == "y":
    client_id = ask("Google Client ID: ")
    secret = getpass.getpass("Google Client Secret: ")

    assert client_id and secret

    provider = {
        "alias": "google",
        "displayName": "Google",
        "providerId": "google",
        "enabled": True,
        "trustEmail": False,
        "storeToken": False,
        "firstBrokerLoginFlowAlias": "first broker login",
        "config": {
            "clientId": client_id,
            "clientSecret": secret,
            "defaultScope": "openid email profile",
            "syncMode": "IMPORT"
        }
    }

    providers = json.loads(kc(
        "get", "identity-provider/instances",
        "-r", realm, "--fields", "alias"
    ))

    exists = any(
        p.get("alias") == "google"
        for p in providers
    )

    endpoint = (
        "identity-provider/instances/google"
        if exists else
        "identity-provider/instances"
    )

    kc(
        "update" if exists else "create",
        endpoint, "-r", realm, "-f", "-",
        data=provider
    )

    print("[PASS] Google OAuth configured")

if os.getenv("CODESPACES") == "true":
    name = os.environ["CODESPACE_NAME"]
    domain = os.environ["GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN"]

    auth_url = f"https://{name}-8081.{domain}"
    frontend = f"https://{name}-5173.{domain}"

    def update_client(client_id, fields):
        clients = json.loads(kc(
            "get", "clients",
            "-r", realm,
            "-q", f"clientId={client_id}",
            "--fields", "id,clientId"
        ))

        clients = [
            c for c in clients
            if c["clientId"] == client_id
        ]

        assert len(clients) == 1

        kc(
            "update", f"clients/{clients[0]['id']}",
            "-r", realm, "-f", "-",
            data=fields
        )

    update_client("account-console", {
        "webOrigins": [
            auth_url,
            "http://localhost:8081"
        ]
    })

    update_client("marketplace-web", {
        "standardFlowEnabled": True,
        "redirectUris": [
            "http://localhost:5173/*",
            f"{frontend}/*"
        ],
        "webOrigins": [
            "http://localhost:5173",
            frontend
        ]
    })

    print("[PASS] Codespaces clients configured")
    print("Google Redirect URI:")
    print(f"{auth_url}/realms/{realm}/broker/google/endpoint")

print("[DONE] Authentication configuration completed")
PY

#!/usr/bin/env python3

import argparse
import getpass
import json
import os
import subprocess
import sys
import uuid
import time

from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import urlencode
from urllib.error import HTTPError, URLError

ROOT = Path(__file__).resolve().parent.parent
API_BASE = os.getenv("API_BASE", "http://localhost:8000").rstrip("/")
KEYCLOAK_URL = os.getenv("KEYCLOAK_URL", "http://localhost:8081").rstrip("/")
API = f"{API_BASE}/api"
AUTH = (
    f"{KEYCLOAK_URL}/realms/marketplace/"
    "protocol/openid-connect/token"
)

parser = argparse.ArgumentParser()
parser.add_argument("--apply", action="store_true")
parser.add_argument("--limit", type=int, default=59)
args = parser.parse_args()

if args.limit < 1:
    parser.error("--limit must be positive")

query = """
SELECT "Id", "Slug"
FROM products
WHERE "Slug" ~ '^steam-[0-9]+$'
AND "SteamMetadataJson" IS NOT NULL
AND "IsActive" = true
ORDER BY "Slug";
"""

output = subprocess.check_output(
    [
        "docker", "compose", "exec", "-T", "postgres",
        "sh", "-c",
        'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" '
        '-d catalog_db -At -F "|"'
    ],
    input=query,
    text=True,
    cwd=ROOT
)

products = []

for line in output.splitlines():
    product_id, slug = line.strip().split("|")
    uuid.UUID(product_id)
    products.append((product_id, slug))

if not products:
    sys.exit("[FAIL] No eligible products")

selected = products[:args.limit]

print("=== DEMO LICENSE SEED ===")
print("Mode:", "IMPORT" if args.apply else "PREVIEW")
print("Eligible products:", len(products))
print("Selected products:", len(selected))
print("Keys per product: 10")
print("Target keys:", len(selected) * 10)

if not args.apply:
    for _, slug in selected:
        print(f"[PLAN] {slug}: 10 demo keys")
    sys.exit(0)

print("\nDevelopment environment only.")
if input("Type TEST to continue: ") != "TEST":
    sys.exit("Cancelled")

username = "license_coordinator"

password = getpass.getpass("Password: ")

def get_token():
    data = urlencode({
        "grant_type": "password",
        "client_id": "marketplace-web",
        "username": username,
        "password": password
    }).encode()

    with urlopen(Request(AUTH, data=data), timeout=20) as response:
        return json.load(response)["access_token"]

try:
    token = get_token()
    print("[OK] Keycloak authentication")
except HTTPError as e:
    sys.exit(f"[FAIL] Authentication HTTP {e.code}")
except (URLError, TimeoutError):
    sys.exit("[FAIL] Authentication connection error")

def import_keys(product_id, keys, token):
    request = Request(
        f"{API}/licenses/import",
        data=json.dumps({
            "productId": product_id,
            "keys": keys
        }).encode(),
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json"
        },
        method="POST"
    )

    with urlopen(request, timeout=30) as response:
        return json.load(response)

passed = failed = imported = duplicates = 0

for i, (product_id, slug) in enumerate(selected, 1):
    keys = [
        f"DEMO-NOT-REDEEMABLE-{product_id}-{n:02d}"
        for n in range(1, 11)
    ]

    print(f"\n[{i}/{len(selected)}] {slug}")

    try:
        try:
            result = import_keys(product_id, keys, token)
        except HTTPError as e:
            if e.code != 401:
                raise
            token = get_token()
            result = import_keys(product_id, keys, token)

        new = int(result["imported"])
        dup = int(result["duplicates"])
        invalid = int(result["invalid"])

        if invalid != 0 or new + dup != 10:
            raise ValueError("Unexpected import counters")

        imported += new
        duplicates += dup
        passed += 1

        print(
            f"[PASS] Imported={new}; "
            f"duplicates={dup}; invalid={invalid}"
        )

    except HTTPError as e:
        print(f"[FAIL] HTTP {e.code}")
        failed += 1
        break

    except Exception as e:
        print(f"[FAIL] {type(e).__name__}")
        failed += 1
        break

    time.sleep(0.3)

print("\n=== SUMMARY ===")
print("Passed:", passed)
print("Failed:", failed)
print("Imported:", imported)
print("Duplicates:", duplicates)

sys.exit(1 if failed else 0)

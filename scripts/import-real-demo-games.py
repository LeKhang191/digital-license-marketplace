#!/usr/bin/env python3
"""Import the 3 real cheap demo Steam games (datasets/demo/real-demo-games.csv)."""
import argparse
import csv
import getpass
import json
import os
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parent.parent
API_BASE = os.getenv("API_BASE", "http://localhost:8000").rstrip("/")
KEYCLOAK_URL = os.getenv("KEYCLOAK_URL", "http://localhost:8081").rstrip("/")
IMPORT_URL = f"{API_BASE}/api/catalog/steam-import"
PRODUCTS_URL = f"{API_BASE}/api/catalog/products"
TOKEN_URL = f"{KEYCLOAK_URL}/realms/marketplace/protocol/openid-connect/token"


def get_token(username: str, password: str) -> str:
    data = urlencode(
        {
            "grant_type": "password",
            "client_id": "marketplace-web",
            "username": username,
            "password": password,
        }
    ).encode("utf-8")
    with urlopen(Request(TOKEN_URL, data=data), timeout=20) as resp:
        return json.load(resp)["access_token"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Import the 3 games via Catalog API")
    parser.add_argument("--activate", action="store_true", help="Set IsActive=true after import")
    parser.add_argument(
        "--csv",
        default=str(ROOT / "datasets/demo/real-demo-games.csv"),
        help="Path to CSV file",
    )
    args = parser.parse_args()

    with Path(args.csv).open(newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))

    print("MODE:", "APPLY" if args.apply else "PREVIEW")
    for r in rows:
        print(f" - steam-{r['app_id']} | {r['name']} | {r['demo_price_vnd']} VND | {r.get('note', '')}")

    if not args.apply:
        return 0

    password = os.getenv("CATALOG_MANAGER_PASSWORD") or getpass.getpass("catalog_manager password: ")
    token = get_token("catalog_manager", password)

    for r in rows:
        app_id = r["app_id"].strip()
        price = int(r["demo_price_vnd"])
        payload = json.dumps({"app": app_id, "price": price}).encode("utf-8")
        req = Request(
            IMPORT_URL,
            data=payload,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {token}",
            },
            method="POST",
        )
        try:
            with urlopen(req, timeout=60) as resp:
                created = json.load(resp)
            print(f"[PASS] Imported steam-{app_id} ({created.get('name')}) -> id={created.get('id')}")
            if args.activate and created.get("id"):
                upd = json.dumps(
                    {
                        "name": created["name"],
                        "slug": created["slug"],
                        "description": created["description"],
                        "category": created["category"],
                        "price": price,
                        "isActive": True,
                    }
                ).encode("utf-8")
                put_req = Request(
                    f"{PRODUCTS_URL}/{created['id']}",
                    data=upd,
                    headers={
                        "Content-Type": "application/json",
                        "Authorization": f"Bearer {token}",
                    },
                    method="PUT",
                )
                with urlopen(put_req, timeout=30):
                    pass
                print(f"[PASS] Activated steam-{app_id}")
        except HTTPError as e:
            if e.code == 409:
                print(f"[SKIP] steam-{app_id} already exists")
            else:
                print(f"[FAIL] steam-{app_id} HTTP {e.code}: {e.read().decode('utf-8', errors='replace')[:200]}")
                return 1
        except (URLError, TimeoutError) as e:
            print(f"[FAIL] steam-{app_id}: {e}")
            return 1
        time.sleep(1.5)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

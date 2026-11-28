#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

echo "====================================="
echo " DLM - Local Demo Data Recovery"
echo "====================================="

if [[ "${CODESPACES:-false}" == "true" ]]; then
    echo "[FAIL] Run this script locally."
    exit 1
fi

test -f .env
docker compose config --quiet

echo "[1/8] Checking infrastructure"

curl -fsS --max-time 15 \
    http://localhost:8081/realms/marketplace/.well-known/openid-configuration \
    >/dev/null

curl -fsS --max-time 15 \
    http://localhost:8000/api/catalog/products \
    >/dev/null

curl -4 -fsS --max-time 25 \
    'https://store.steampowered.com/api/appdetails?appids=730' |
    jq -e '."730".success == true' >/dev/null

echo "[PASS] Infrastructure and Steam API"

echo "[2/8] Importing game dataset"

IMPORT_RC=0

python3 -u scripts/seed-game-dataset.py --apply \
    2>&1 | tee dlm-game-import-local.txt || IMPORT_RC=$?

if (( IMPORT_RC != 0 )); then
    echo "[WARN] Some games could not be imported."
    echo "Review import results above."

    read -rp "Continue with imported games? Type TEST: " CONFIRM
    [[ "$CONFIRM" == "TEST" ]] || exit 1
fi

echo "[3/8] Activating imported dataset"

python3 - <<'PY'
import csv
import subprocess
from pathlib import Path

with Path("datasets/demo/steam-games.csv").open() as f:
    rows = list(csv.DictReader(f))

ids = [r["app_id"] for r in rows]

assert ids and all(x.isdigit() for x in ids)

slugs = ",".join("'steam-" + x + "'" for x in ids)

sql = f"""
BEGIN;

UPDATE products
SET "IsActive" = true
WHERE "Slug" IN ({slugs})
AND "SteamMetadataJson" IS NOT NULL
AND "IsActive" = false;

SELECT COUNT(*) AS active_dataset_games
FROM products
WHERE "Slug" IN ({slugs})
AND "SteamMetadataJson" IS NOT NULL
AND "IsActive" = true;

COMMIT;
"""

subprocess.run(
    ["docker", "compose", "exec", "-T", "postgres",
     "psql", "-X", "-v", "ON_ERROR_STOP=1",
     "-U", "app", "-d", "catalog_db"],
    input=sql,
    text=True,
    check=True
)
PY

echo "[4/8] Importing demo licenses"

python3 -u scripts/seed-demo-licenses.py \
    --apply --limit 60 \
    2>&1 | tee dlm-license-import-local.txt

echo "[5/8] Checking duplicate protection"

python3 -u scripts/seed-demo-licenses.py \
    --apply --limit 1 \
    2>&1 | tee dlm-license-duplicate-local.txt

grep -Fq 'Imported=0; duplicates=10; invalid=0' \
    dlm-license-duplicate-local.txt || {
        echo "[FAIL] Duplicate verification"
        exit 1
    }

echo "[PASS] Duplicate protection"

echo "[6/8] Checking inventory and configuring Kinguin Mock"

python3 - <<'PY'
import csv
import re
import subprocess
import uuid
from pathlib import Path

def query(db, sql):
    result = subprocess.check_output(
        [
            "docker", "compose", "exec", "-T", "postgres",
            "psql", "-X", "-At",
            "-v", "ON_ERROR_STOP=1",
            "-U", "app", "-d", db, "-F", "|"
        ],
        input=sql,
        text=True
    )

    return result.strip().splitlines()

with Path("datasets/demo/steam-games.csv").open() as f:
    dataset = {
        "steam-" + row["app_id"]
        for row in csv.DictReader(f)
    }

catalog = query("catalog_db", """
SELECT "Id", "Slug"
FROM products
WHERE "IsActive" = true
AND "SteamMetadataJson" IS NOT NULL
ORDER BY "Slug";
""")

inventory = query("license_db", """
SELECT "ProductId", COUNT(*)
FROM license_keys
WHERE "Status" = 'AVAILABLE'
GROUP BY "ProductId";
""")

stock = {}

for row in inventory:
    pid, count = row.split("|")
    stock[pid] = int(count)

products = []

for row in catalog:
    pid, slug = row.split("|")

    if slug not in dataset:
        continue

    uuid.UUID(pid)
    products.append((pid, slug))

if len(products) < 3:
    raise SystemExit("[FAIL] Not enough imported games")

missing = [
    slug for pid, slug in products
    if stock.get(pid, 0) < 10
]

if missing:
    print("[FAIL] Insufficient license inventory:")
    for slug in missing:
        print(slug)
    raise SystemExit(1)

print("[PASS] Active games:", len(products))
print("[PASS] Available licenses:",
      sum(stock.get(pid, 0) for pid, _ in products))

selected = products[:3]

p = Path(".env")
s = p.read_text()

def save(key, value):
    global s

    pattern = rf"^{re.escape(key)}=.*$"
    entry = f"{key}={value}"

    if re.search(pattern, s, re.M):
        s = re.sub(pattern, lambda _: entry, s, flags=re.M)
    else:
        s = s.rstrip() + "\n" + entry + "\n"

save("KINGUIN_MODE", "Mock")
save("KINGUIN_LOCAL_PRODUCT_ID", selected[0][0])
save("KINGUIN_PRODUCT_ID", "demo-product-001")

save(
    "KINGUIN_PRODUCT_MAPPINGS",
    ";".join(
        f"{pid}=demo-product-{i:03d}"
        for i, (pid, slug) in enumerate(selected[1:], 2)
    )
)

p.write_text(s)
p.chmod(0o600)

for i, (pid, slug) in enumerate(selected, 1):
    print(f"[PASS] {slug}")
    print(f"       Product UUID: {pid}")
    print(f"       Available: {stock[pid]}")
    print(f"       Supplier: demo-product-{i:03d}")

print("[PASS] Kinguin Mock configuration saved")
PY

echo "[7/8] Applying environment configuration"

docker compose config --quiet
docker compose up -d

echo "[8/8] Final verification"

docker compose ps

echo "=== KINGUIN MAPPING ==="

grep -E '^KINGUIN_(MODE|LOCAL_PRODUCT_ID|PRODUCT_ID|PRODUCT_MAPPINGS)=' \
    .env

echo "=== RECOVERY COMPLETE ==="
echo "[PASS] Demo data recovery completed"

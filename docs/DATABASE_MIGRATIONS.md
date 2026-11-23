# Phase 2: EF Core Migrations (Catalog + Order)

## Rules
- **Never** use `docker compose down -v`, `EnsureDeleted`, or run `InitialCreate.Up()` on a populated database.
- Code now **does not** create/alter tables on app startup. Database provisioning is a separate, reviewed operation.
- `license_db` is outside this phase: License Vault has no `DbContext` yet.
- Keep backups **outside Git** and secure the files (`umask 077`).

## 1. Generate migration source on Arch Linux (requires .NET 10 SDK / NuGet)

```bash
git switch -c fix/database-migrations
# Install dotnet-ef matching EF Core 10 if missing:
dotnet tool install --global dotnet-ef --version 10.0.3
bash scripts/phase2-generate-migrations.sh
dotnet build DigitalLicenseMarketplace.slnx
dotnet ef migrations list --project services/order-service/Order.Api.csproj --no-connect
dotnet ef migrations list --project services/catalog-service/Catalog.Api.csproj --no-connect
```

Review both `*InitialCreate.cs` Up/Down functions and `*ModelSnapshot.cs` files before applying changes. Commit generated source. Do **not** deploy these changes to a fresh database before applying the initial migration: startup no longer creates the tables.

## 2. Backup populated PostgreSQL before baseline

```bash
mkdir -p backups && chmod 700 backups
umask 077
docker exec digital-license-marketplace-postgres-1 pg_dump -U app -d catalog_db -Fc > backups/catalog-before-baseline.dump
docker exec digital-license-marketplace-postgres-1 pg_dump -U app -d order_db -Fc > backups/order-before-baseline.dump
test -s backups/catalog-before-baseline.dump && test -s backups/order-before-baseline.dump
```

Test restoring these backups into temporary databases separately before production use.

## 3. Verify existing schema (required before marking an existing DB baselined)

```bash
docker exec -i digital-license-marketplace-postgres-1 psql -U app -d catalog_db -v ON_ERROR_STOP=1 < infra/postgres/phase2-preflight.sql
docker exec -i digital-license-marketplace-postgres-1 psql -U app -d order_db -v ON_ERROR_STOP=1 < infra/postgres/phase2-preflight.sql
```

Expected older `order_db` may FAIL because `PaymentRef`, `VnpTransactionNo`, `VnpPayDate` were added as `text`, but EF declares `varchar(64/32/14)`. Resolve with a **reviewed** SQL alteration after checking current values will fit, e.g.:

```sql
SELECT max(length("PaymentRef")), max(length("VnpTransactionNo")), max(length("VnpPayDate")) FROM orders;
-- Only if safe and backed up:
ALTER TABLE orders
  ALTER COLUMN "PaymentRef" TYPE varchar(64),
  ALTER COLUMN "VnpTransactionNo" TYPE varchar(32),
  ALTER COLUMN "VnpPayDate" TYPE varchar(14);
CREATE INDEX IF NOT EXISTS "IX_orders_PaymentRef" ON orders ("PaymentRef");
```

**The preflight checks primary columns, nullability, selected indexes/FKs, but is not an exhaustive catalog comparison.** Review the generated SQL and actual schema, including constraints/defaults, before baseline. Check for a migration-history table from any previous migration. `EnsureCreated` databases generally have none.

## 4. New, empty database: apply InitialCreate normally

Use a migration connection with DDL privileges, not the app's long-lived runtime credentials:

```bash
# Outside Docker, arrange access to PostgreSQL (e.g., temporary localhost port binding) and export a migration-only connection string:
export EF_MIGRATIONS_CONNECTION='Host=localhost;Port=5432;Database=<EMPTY_DATABASE>;Username=<MIGRATOR>;Password=<SECRET>'
dotnet ef database update --project services/order-service/Order.Api.csproj --startup-project services/order-service/Order.Api.csproj
unset EF_MIGRATIONS_CONNECTION
```

Use the Catalog project/connection for `catalog_db`. Do not use this on an existing EnsureCreated database.

## 5. Existing populated DB: baseline, not Up()

Only after successful backups, preflight **and** comparison of generated InitialCreate SQL vs the actual tables, mark the initial migration as already applied by creating `__EFMigrationsHistory` and inserting the **exact** migration ID generated in step 1, with the EF Core product version. Use a reviewed transaction, e.g.:

```sql
BEGIN;
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
  "MigrationId" varchar(150) NOT NULL PRIMARY KEY,
  "ProductVersion" varchar(32) NOT NULL
);
-- Replace values after inspecting the generated migration source:
-- INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
-- VALUES ('<EXACT_INITIALCREATE_ID>', '10.0.3');
COMMIT;
```

**Do not paste a placeholder INSERT.** Baseline is a metadata acknowledgement, not a schema repair. Validate exact migration ID and version from generated source, and ensure all tables, columns, constraints and indexes match first. An incorrect baseline permanently hides schema drift.

After baseline: use `dotnet ef migrations add <ChangeName>` and apply subsequent migrations in a controlled deployment step, preferably an offline-reviewed idempotent SQL script. Never apply schema changes automatically from API startup.

## 6. Regression evidence

```bash
dotnet build DigitalLicenseMarketplace.slnx
docker compose config --quiet
# After baseline, deploy new image:
docker compose up -d --build catalog order
# Authenticate with a fresh JWT and verify:
curl -sS -o /tmp/orders-test.json -w 'Order API HTTP: %{http_code}\n' \
  http://localhost:8000/api/orders -H "Authorization: Bearer $TOKEN"
# Expect 200 with valid token, and no schema exception in logs.
docker compose logs --tail=100 order catalog
```

Rollback: restore the backed-up database with a reviewed restore plan; reverting C# alone does not undo applied database schema changes.

#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
command -v dotnet >/dev/null || { echo 'dotnet SDK required' >&2; exit 1; }
if ! dotnet ef --version >/dev/null 2>&1; then
  echo 'Install dotnet-ef matching EF Core 10: dotnet tool install --global dotnet-ef --version 10.0.3' >&2
  exit 1
fi
if find services/catalog-service services/order-service -type f -name '*InitialCreate*.cs' | grep -q .; then
  echo 'Initial migrations seem to exist; refusing to generate duplicates.' >&2
  exit 1
fi
dotnet ef migrations add InitialCreate --project services/catalog-service/Catalog.Api.csproj --startup-project services/catalog-service/Catalog.Api.csproj --context CatalogDbContext --output-dir Infrastructure/Persistence/Migrations
dotnet ef migrations add InitialCreate --project services/order-service/Order.Api.csproj --startup-project services/order-service/Order.Api.csproj --context OrderDbContext --output-dir Infrastructure/Persistence/Migrations
echo 'Generated migrations. REVIEW Up/Down + snapshots and commit before running against any database.'

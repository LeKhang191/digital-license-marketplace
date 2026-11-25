
#!/usr/bin/env bash

set -Eeuo pipefail

cd /src

: "${CATALOG_CONNECTION:?Catalog connection is required}"
: "${ORDER_CONNECTION:?Order connection is required}"

case "${1:-}" in
    catalog)
        echo "[Migration] Updating Catalog database..."

        EF_MIGRATIONS_CONNECTION="$CATALOG_CONNECTION" \
        dotnet ef database update \
            --project services/catalog-service/Catalog.Api.csproj \
            --startup-project services/catalog-service/Catalog.Api.csproj \
            --context CatalogDbContext \
            --configuration Release \
            --no-build
        ;;

    order)
        echo "[Migration] Updating Order database..."

        EF_MIGRATIONS_CONNECTION="$ORDER_CONNECTION" \
        dotnet ef database update \
            --project services/order-service/Order.Api.csproj \
            --startup-project services/order-service/Order.Api.csproj \
            --context OrderDbContext \
            --configuration Release \
            --no-build
        ;;

    *)
        echo "Usage: migrate.sh {catalog|order}" >&2
        exit 1
        ;;
esac

echo "[Migration] Completed successfully."

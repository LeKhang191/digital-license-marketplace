
#!/usr/bin/env bash

set -Eeuo pipefail

cd /src

case "${1:-}" in
    catalog)
        : "${CATALOG_CONNECTION:?Catalog connection is required}"

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
        : "${ORDER_CONNECTION:?Order connection is required}"

        echo "[Migration] Updating Order database..."

        EF_MIGRATIONS_CONNECTION="$ORDER_CONNECTION" \
        dotnet ef database update \
            --project services/order-service/Order.Api.csproj \
            --startup-project services/order-service/Order.Api.csproj \
            --context OrderDbContext \
            --configuration Release \
            --no-build
        ;;

    license)
        : "${LICENSE_CONNECTION:?License connection is required}"

        echo "[Migration] Updating License database..."

        EF_MIGRATIONS_CONNECTION="$LICENSE_CONNECTION" \
        dotnet ef database update \
            --project services/license-vault-service/LicenseVault.Api.csproj \
            --startup-project services/license-vault-service/LicenseVault.Api.csproj \
            --context LicenseDbContext \
            --configuration Release \
            --no-build
        ;;

    payment)
        : "${PAYMENT_CONNECTION:?Payment connection is required}"

        echo "[Migration] Updating Payment database..."

        EF_MIGRATIONS_CONNECTION="$PAYMENT_CONNECTION" \
        dotnet ef database update \
            --project services/payment-service/Payment.Api.csproj \
            --startup-project services/payment-service/Payment.Api.csproj \
            --context PaymentDbContext \
            --configuration Release \
            --no-build
        ;;

    *)
        echo "Usage: migrate.sh {catalog|order|license|payment}" >&2
        exit 1
        ;;
esac

echo "[Migration] Completed successfully."

#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."

C=services/catalog-service
CTRL=$C/Controllers/ProductsController.cs

echo "1/4 Add package JwtBearer for Catalog..."
dotnet add $C package Microsoft.AspNetCore.Authentication.JwtBearer --version 10.0.14

echo "2/4 Change API key to admin role in ProductsController..."
sed -i 's|\[ServiceFilter(typeof(AdminApiKeyFilter))\]|[Authorize(Policy = "AdminOnly")]|' $CTRL
grep -q 'Microsoft.AspNetCore.Authorization' $CTRL || \
  sed -i '1i using Microsoft.AspNetCore.Authorization;' $CTRL

echo "3/4 Remove AdminApiKeyFilter (no longer used)..."
rm -f $C/Infrastructure/Security/AdminApiKeyFilter.cs

echo "4/4 Checking build..."
dotnet build $C

echo "Done."

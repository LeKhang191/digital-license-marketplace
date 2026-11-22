#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."

C=services/catalog-service
O=services/order-service
CSPROJ=$C/Catalog.Api.csproj

JWT=$(grep -oP 'JwtBearer" Version="\K[^"]+' $CSPROJ)
NPG=$(grep -oP 'Npgsql.EntityFrameworkCore.PostgreSQL" Version="\K[^"]+' $CSPROJ)
echo "JwtBearer=$JWT  Npgsql=$NPG"

echo "1/4 CLean example code for Order and Contracts..."
rm -f $O/WeatherForecast.cs $O/Controllers/WeatherForecastController.cs $O/*.http
rm -f shared/Contracts/Class1.cs

echo "2/4 Put RootNamespace = OrderService..."
grep -q RootNamespace $O/Order.Api.csproj || \
  sed -i 's|<ImplicitUsings>enable</ImplicitUsings>|<ImplicitUsings>enable</ImplicitUsings>\n    <RootNamespace>OrderService</RootNamespace>|' $O/Order.Api.csproj

echo "3/4 Adding package for Order..."
dotnet add $O package Npgsql.EntityFrameworkCore.PostgreSQL --version "$NPG"
dotnet add $O package Microsoft.AspNetCore.Authentication.JwtBearer --version "$JWT"

echo "4/4 Build..."
dotnet build $O
dotnet build shared/Contracts

echo "Done."

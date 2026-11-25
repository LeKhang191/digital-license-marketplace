
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY shared/Contracts/Contracts.csproj shared/Contracts/
COPY services/license-vault-service/LicenseVault.Api.csproj services/license-vault-service/

RUN dotnet restore services/license-vault-service/LicenseVault.Api.csproj

COPY shared/Contracts/ shared/Contracts/
COPY services/license-vault-service/ services/license-vault-service/

RUN dotnet publish services/license-vault-service/LicenseVault.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "LicenseVault.Api.dll"]

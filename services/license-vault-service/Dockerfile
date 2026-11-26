FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/Contracts/Contracts.csproj shared/Contracts/
COPY services/license-vault-service/LicenseVault.Api.csproj services/license-vault-service/
RUN dotnet restore services/license-vault-service/LicenseVault.Api.csproj

COPY shared/Contracts/ shared/Contracts/
COPY services/license-vault-service/ services/license-vault-service/

RUN find /src/services/license-vault-service /src/shared/Contracts -type d \
    \( -name 'bin?Debug' -o -name 'bin?Release' \) \
    -prune -print -exec rm -rf {} +

RUN dotnet publish services/license-vault-service/LicenseVault.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

USER root

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LicenseVault.Api.dll"]

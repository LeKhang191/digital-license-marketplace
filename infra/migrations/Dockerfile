
FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /src

ENV PATH="/root/.dotnet/tools:${PATH}"

RUN dotnet tool install --global dotnet-ef --version 10.0.3

COPY shared/Contracts/Contracts.csproj shared/Contracts/
COPY services/catalog-service/Catalog.Api.csproj services/catalog-service/
COPY services/order-service/Order.Api.csproj services/order-service/

RUN dotnet restore services/catalog-service/Catalog.Api.csproj \
    && dotnet restore services/order-service/Order.Api.csproj

COPY shared/Contracts/ shared/Contracts/
COPY services/catalog-service/ services/catalog-service/
COPY services/order-service/ services/order-service/

RUN dotnet build services/catalog-service/Catalog.Api.csproj \
        -c Release --no-restore \
    && dotnet build services/order-service/Order.Api.csproj \
        -c Release --no-restore

COPY infra/migrations/migrate.sh /usr/local/bin/migrate.sh

ENTRYPOINT ["bash", "/usr/local/bin/migrate.sh"]

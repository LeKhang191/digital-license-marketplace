FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/Contracts/Contracts.csproj shared/Contracts/
COPY services/order-service/Order.Api.csproj services/order-service/
RUN dotnet restore services/order-service/Order.Api.csproj

COPY shared/Contracts/ shared/Contracts/
COPY services/order-service/ services/order-service/

RUN dotnet publish services/order-service/Order.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

USER root

RUN apt-get update && \
    apt-get install -y --no-install-recommends libgssapi-krb5-2 && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Order.Api.dll"]

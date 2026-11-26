FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/Contracts/Contracts.csproj shared/Contracts/
COPY services/payment-service/Payment.Api.csproj services/payment-service/
RUN dotnet restore services/payment-service/Payment.Api.csproj

COPY shared/Contracts/ shared/Contracts/
COPY shared/Infrastructure/ shared/Infrastructure/
COPY services/payment-service/ services/payment-service/

RUN dotnet publish services/payment-service/Payment.Api.csproj \
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

ENTRYPOINT ["dotnet", "Payment.Api.dll"]

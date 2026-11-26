FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY shared/Contracts/Contracts.csproj shared/Contracts/
COPY services/notification-service/Notification.Worker.csproj services/notification-service/

RUN dotnet restore services/notification-service/Notification.Worker.csproj

COPY shared/Contracts/ shared/Contracts/
COPY services/notification-service/ services/notification-service/

RUN dotnet publish services/notification-service/Notification.Worker.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0

WORKDIR /app

USER root

RUN apt-get update && \
    apt-get install -y --no-install-recommends libgssapi-krb5-2 && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "Notification.Worker.dll"]

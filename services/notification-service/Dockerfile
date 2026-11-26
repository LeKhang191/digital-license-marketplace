FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/Contracts/Contracts.csproj shared/Contracts/
COPY services/notification-service/Notification.Worker.csproj services/notification-service/
RUN dotnet restore services/notification-service/Notification.Worker.csproj

COPY shared/Contracts/ shared/Contracts/
COPY services/notification-service/ services/notification-service/

RUN find /src/services/notification-service /src/shared/Contracts -type d \
    \( -name 'bin?Debug' -o -name 'bin?Release' \) \
    -prune -print -exec rm -rf {} +

RUN dotnet publish services/notification-service/Notification.Worker.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "Notification.Worker.dll"]

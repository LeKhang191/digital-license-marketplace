FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/catalog-service/Catalog.Api.csproj services/catalog-service/
RUN dotnet restore services/catalog-service/Catalog.Api.csproj

COPY services/catalog-service/ services/catalog-service/

# Remove malformed Windows build directories on Linux
RUN find /src/services/catalog-service -type d \
    \( -name 'bin?Debug' -o -name 'bin?Release' \) \
    -prune -print -exec rm -rf {} +

RUN dotnet publish services/catalog-service/Catalog.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Catalog.Api.dll"]

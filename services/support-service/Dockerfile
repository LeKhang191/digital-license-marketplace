FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/support-service/Support.Api.csproj services/support-service/

RUN dotnet restore services/support-service/Support.Api.csproj

COPY shared/Infrastructure/ shared/Infrastructure/
COPY services/support-service/ services/support-service/

RUN dotnet publish services/support-service/Support.Api.csproj     -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

USER root

RUN apt-get update &&     apt-get install -y --no-install-recommends libgssapi-krb5-2 &&     rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Support.Api.dll"]

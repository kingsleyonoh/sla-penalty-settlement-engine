# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore Slapen.sln
RUN dotnet build Slapen.sln --configuration Release --no-restore
RUN dotnet publish src/Slapen.Api/Slapen.Api.fsproj --configuration Release --no-build --output /app/publish
RUN dotnet publish db/Migrate/Slapen.DbMigrate.fsproj --configuration Release --no-build --output /app/migrate

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5109
EXPOSE 5109

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=build /app/migrate ./migrate
COPY db ./db

HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD curl -fsS http://localhost:5109/api/health || exit 1

ENTRYPOINT ["/bin/sh", "-c", "if [ \"${AUTO_MIGRATE:-true}\" = \"true\" ]; then dotnet /app/migrate/Slapen.DbMigrate.dll; fi; exec dotnet Slapen.Api.dll"]

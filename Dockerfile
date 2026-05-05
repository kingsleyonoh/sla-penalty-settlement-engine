# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet tool restore || true
RUN dotnet restore Slapen.sln
RUN dotnet publish src/Slapen.Api/Slapen.Api.fsproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:5109
EXPOSE 5109

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Slapen.Api.dll"]

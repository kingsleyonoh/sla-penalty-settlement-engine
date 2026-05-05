# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS foundation
WORKDIR /workspace

COPY . .
RUN dotnet restore Slapen.sln
RUN dotnet build Slapen.sln --configuration Release --no-restore

CMD ["dotnet", "test", "Slapen.sln", "--configuration", "Release", "--no-build"]

# syntax=docker/dockerfile:1

# ---- Build stage: has the full .NET SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Copy only the project file and restore packages. Docker caches this layer and reuses it
#    until CreditCheck.Worker.csproj changes, so editing code never triggers a restore.
#
#    The cache mount keeps NuGet's package folder between builds, outside the image. When the
#    .csproj does change, only new packages are downloaded. `sharing=locked` makes parallel
#    builds (docker compose builds both services at once) take turns instead of writing to the
#    folder at the same time.
COPY CreditCheck.Worker/CreditCheck.Worker.csproj CreditCheck.Worker/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore CreditCheck.Worker/CreditCheck.Worker.csproj

# 2. Copy the source and publish. Publish restores again, but with packages already in place
#    that's a quick check. It also re-downloads anything missing if the cache was cleared.
COPY CreditCheck.Worker/ CreditCheck.Worker/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish CreditCheck.Worker/CreditCheck.Worker.csproj --configuration Release --output /app

# ---- Runtime stage: only the .NET runtime, no SDK and no ASP.NET Core ----
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .

# The .NET images come with a non-root user. APP_UID is its ID.
USER $APP_UID

ENTRYPOINT ["dotnet", "CreditCheck.Worker.dll"]

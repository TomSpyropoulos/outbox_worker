# syntax=docker/dockerfile:1

# ---- Build stage: has the full .NET SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Copy only the project file and restore packages. Docker caches this layer and reuses it
#    until Loans.Api.csproj changes, so editing code never triggers a restore.
#
#    The cache mount keeps NuGet's package folder between builds, outside the image. When the
#    .csproj does change, only new packages are downloaded. `sharing=locked` makes parallel
#    builds (docker compose builds both services at once) take turns instead of writing to the
#    folder at the same time.
COPY Loans.Api/Loans.Api.csproj Loans.Api/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore Loans.Api/Loans.Api.csproj

# 2. Copy the source and publish. Publish restores again, but with packages already in place
#    that's a quick check. It also re-downloads anything missing if the cache was cleared.
COPY Loans.Api/ Loans.Api/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish Loans.Api/Loans.Api.csproj --configuration Release --output /app

# ---- Runtime stage: only the ASP.NET Core runtime, no SDK, so the image is much smaller ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# The .NET images come with a non-root user. APP_UID is its ID.
USER $APP_UID

# .NET images listen on port 8080 by default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Loans.Api.dll"]

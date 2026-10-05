# syntax=docker/dockerfile:1.7
# Build context: repository root (needs global.json and spec/).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json ./
COPY server/Directory.Build.props server/
COPY server/src/FeatureFlare.Domain/FeatureFlare.Domain.csproj server/src/FeatureFlare.Domain/
COPY server/src/FeatureFlare.Application/FeatureFlare.Application.csproj server/src/FeatureFlare.Application/
COPY server/src/FeatureFlare.Infrastructure/FeatureFlare.Infrastructure.csproj server/src/FeatureFlare.Infrastructure/
COPY server/src/FeatureFlare.Api/FeatureFlare.Api.csproj server/src/FeatureFlare.Api/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore server/src/FeatureFlare.Api/FeatureFlare.Api.csproj

COPY server/src server/src
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish server/src/FeatureFlare.Api/FeatureFlare.Api.csproj \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

# The official images ship a non-root "app" user.
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "FeatureFlare.Api.dll"]

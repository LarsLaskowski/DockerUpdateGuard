ARG BASE_SDK="mcr.microsoft.com/dotnet/sdk:10.0-alpine"
ARG BASE_RUNTIME="mcr.microsoft.com/dotnet/aspnet:10.0-alpine"
ARG APP_VERSION=1.0.0

# ---------------------------
# Build Stage
# ---------------------------
FROM ${BASE_SDK} AS build
ARG APP_VERSION
ARG BASE_SDK

LABEL org.opencontainers.image.base.name="${BASE_SDK}"
LABEL org.opencontainers.image.base.digest="${BASE_SDK_DIGEST}"

WORKDIR /src

COPY Directory.Packages.props ./
COPY Directory.Build.props ./
COPY src/DockerUpdateGuard/DockerUpdateGuard.csproj src/DockerUpdateGuard/
COPY src/DockerUpdateGuard.Data/DockerUpdateGuard.Data.csproj src/DockerUpdateGuard.Data/
COPY src/DockerUpdateGuard.Telemetry/DockerUpdateGuard.Telemetry.csproj src/DockerUpdateGuard.Telemetry/
RUN dotnet restore src/DockerUpdateGuard/DockerUpdateGuard.csproj

COPY . .
RUN dotnet publish src/DockerUpdateGuard/DockerUpdateGuard.csproj -c Release -o /app/publish -p:Version=$APP_VERSION -p:InformationalVersion=$APP_VERSION

# ---------------------------
# Runtime Stage
# ---------------------------
FROM ${BASE_RUNTIME}
ARG APP_VERSION
ARG BASE_RUNTIME

LABEL org.opencontainers.image.base.name="${BASE_RUNTIME}"
LABEL org.opencontainers.image.base.digest="${BASE_RUNTIME_DIGEST}"

WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DockerUpdateGuard__DisplayVersion=$APP_VERSION

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Copy startup script that imports any mounted CA certificates and starts the app
COPY src/DockerUpdateGuard/entrypoint.sh .
RUN chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]

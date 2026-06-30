ARG BASE_SDK="mcr.microsoft.com/dotnet/sdk:10.0-alpine"
ARG BASE_RUNTIME="mcr.microsoft.com/dotnet/aspnet:10.0-alpine"
ARG BASE_RUNTIME_DIGEST
ARG APP_VERSION=1.0.0

# ---------------------------
# Build Stage
# ---------------------------
FROM ${BASE_SDK} AS build
ARG APP_VERSION

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
ARG BASE_RUNTIME_DIGEST

LABEL org.opencontainers.image.base.name="${BASE_RUNTIME}"
LABEL org.opencontainers.image.base.digest="${BASE_RUNTIME_DIGEST}"

WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DockerUpdateGuard__DisplayVersion=$APP_VERSION

# Install CA certificates to enable secure outbound connections (e.g., for telemetry)
# apt-get is not available in Alpine, so we use apk to install ca-certificates
#RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
RUN apk add --no-cache ca-certificates

COPY --from=build /app/publish .

# Copy startup script that imports any mounted CA certificates and starts the app
COPY src/DockerUpdateGuard/entrypoint.sh .
# Make the entrypoint executable and run as a dedicated non-root user (UID 64000) in the
# root group (GID 0) to reduce the container blast radius. The root group keeps a host
# Docker socket owned by root:root readable without extra privileges (e.g. on Synology DSM)
# while still avoiding a root UID. /app is group-owned and group permissions mirror the
# owner so the arbitrary UID retains read/write access. The optional CA import in
# entrypoint.sh degrades gracefully when the trust store is not writable.
RUN chmod +x /app/entrypoint.sh \
 && adduser -u 64000 -S -G root -H -h /app dockerupdateguard \
 && chown -R 64000:0 /app \
 && chmod -R g=u /app

USER 64000:0

ENTRYPOINT ["/app/entrypoint.sh"]

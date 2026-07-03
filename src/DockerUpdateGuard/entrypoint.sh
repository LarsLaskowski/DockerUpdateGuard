#!/bin/sh

# Entrypoint script for DockerUpdateGuard
# - Verifies mounted appsettings files are readable by the container user before starting the app,
#   so a host-side permission problem surfaces as a clear message instead of a .NET stack trace.
# - If /app/certs contains .crt/.pem files and the container can write the system trust store,
#   the certificates are imported into /usr/local/share/ca-certificates and update-ca-certificates is run.
# - The script is tolerant: if no certs are provided or the container lacks permission, it continues.

# Fail fast with a clear message if a mounted appsettings file is not readable by this user
for config_file in /app/appsettings.json /app/appsettings."${ASPNETCORE_ENVIRONMENT:-Production}".json; do
  if [ -f "$config_file" ] && [ ! -r "$config_file" ]; then
    echo "ERROR: '$config_file' exists but is not readable by uid=$(id -u) gid=$(id -g)." >&2
    echo "The container runs as a non-root user (UID 64000, GID 0) and needs read access to the mounted file." >&2
    echo "Fix the host-side permissions, e.g. 'chmod 644 <host-path>' or 'chown <host-path> to gid 0'." >&2
    echo "For SMB/CIFS mounts (e.g. Synology shares), pass mount options such as 'file_mode=0644,dir_mode=0755' so the file is world- or group-readable." >&2
    exit 1
  fi
done

# Do not fail the container if certificate import fails

# Check for certs directory and files
if [ -d "/app/certs" ]; then
  cert_files=$(ls /app/certs/* 2>/dev/null || true)
  if [ -n "$cert_files" ]; then
    echo "Found certificate files in /app/certs; attempting import into system trust store..."

    # Only attempt to import when running as root or when trust store is writable
    if [ "$(id -u)" = "0" ] || [ -w /usr/local/share/ca-certificates ] ; then
      mkdir -p /usr/local/share/ca-certificates/dockerupdateguard

      for cert in /app/certs/*; do
        if [ -f "$cert" ]; then
          case "$cert" in
            *.crt|*.pem)
              cp "$cert" /usr/local/share/ca-certificates/dockerupdateguard/$(basename "$cert") 2>/dev/null || echo "Warning: failed to copy $cert"
              ;;
            *)
              echo "Skipping non-certificate file: $cert"
              ;;
          esac
        fi
      done

      # Update system CA bundle (if available)
      if command -v update-ca-certificates >/dev/null 2>&1; then
        update-ca-certificates || echo "Warning: update-ca-certificates failed"
      else
        echo "update-ca-certificates not found; skipping CA bundle update"
      fi
    else
      echo "No permission to write system trust store; skipping certificate import"
    fi
  else
    echo "No certificate files found in /app/certs"
  fi
fi

# Start the application
exec dotnet DockerUpdateGuard.dll "$@"

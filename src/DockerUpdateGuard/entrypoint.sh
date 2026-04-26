#!/bin/sh

# Entrypoint script for DockerUpdateGuard
# - If /app/certs contains .crt/.pem files and the container can write the system trust store,
#   the certificates are imported into /usr/local/share/ca-certificates and update-ca-certificates is run.
# - The script is tolerant: if no certs are provided or the container lacks permission, it continues.

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

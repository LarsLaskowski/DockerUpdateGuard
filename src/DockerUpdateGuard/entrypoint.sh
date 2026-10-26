#!/bin/sh

# Entrypoint script for DockerUpdateGuard
# - Verifies mounted appsettings files are readable by the container user before starting the app,
#   so a host-side permission problem surfaces as a clear message instead of a .NET stack trace.
# - Trusts the CA certificates mounted into /app/certs for every user, root included: each complete PEM
#   certificate block of the .crt/.pem files is written to its own file in a new private directory under /tmp,
#   which is appended to SSL_CERT_DIR (after a pre-set value, else after /etc/ssl/certs). The system trust
#   store, SSL_CERT_FILE and update-ca-certificates are never used, so no write access to the system trust
#   store is needed. The application, its database driver and Trivy read SSL_CERT_DIR.
# - The script is tolerant: if no usable certificates are provided or /tmp is not writable, it prints a
#   message and continues with the system CA certificates only.

# Fail fast with a clear message if a mounted appsettings file is not readable by this user
for config_file in /app/appsettings.json /app/appsettings."${ASPNETCORE_ENVIRONMENT:-Production}".json; do
  if test -f "$config_file" && test ! -r "$config_file"; then
    echo "ERROR: '$config_file' exists but is not readable by uid=$(id -u) gid=$(id -g)." >&2
    echo "The container runs as a non-root user (UID 64000, GID 0) and needs read access to the mounted file." >&2
    echo "Fix the host-side permissions, e.g. 'chmod 644 <host-path>' or 'chown <host-path> to gid 0'." >&2
    echo "For SMB/CIFS mounts (e.g. Synology shares), pass mount options such as 'file_mode=0644,dir_mode=0755' so the file is world- or group-readable." >&2
    exit 1
  fi
done

# Trust the CA certificates mounted into /app/certs (never fails the container)
certs_dir=/app/certs
if test ! -d "$certs_dir"; then
  echo "No /app/certs directory; using the system CA certificates only"
else
  found_files=0
  for cert in "$certs_dir"/*; do
    if test -f "$cert"; then
      found_files=1
    fi
  done

  if test "$found_files" = "0"; then
    echo "No certificate files found in /app/certs; using the system CA certificates only"
  elif cert_dir=$(mktemp -d /tmp/dockerupdateguard-ca.XXXXXX 2>/dev/null) && test -d "$cert_dir"; then
    total_files=0
    total_blocks=0
    file_number=0
    cert_failed=0

    for cert in "$certs_dir"/*; do
      if test ! -f "$cert"; then
        continue
      fi

      file_number=$((file_number + 1))
      case "$cert" in
        *.crt|*.pem)
          if test ! -r "$cert"; then
            echo "Warning: cannot read $cert; skipping it"
            continue
          fi

          # One output file per accepted block: exactly the standard marker lines around base64 body lines
          if block_count=$(awk -v dir="$cert_dir" -v n="$file_number" '
            BEGIN { count = 0; open = 0; body_lines = 0; body = "" }
            {
              line = $0
              if (NR == 1) { sub(/^\357\273\277/, "", line) }
              sub(/[ \t\r]+$/, "", line)
              if (line == "-----BEGIN CERTIFICATE-----") { open = 1; body_lines = 0; body = ""; next }
              if (open == 0) { next }
              if (line == "-----END CERTIFICATE-----") {
                if (body_lines > 0) {
                  count++
                  out = dir "/" n "-" count ".pem"
                  printf "-----BEGIN CERTIFICATE-----\n%s-----END CERTIFICATE-----\n", body > out
                  close(out)
                }
                open = 0
                next
              }
              if (line ~ /^[A-Za-z0-9+\/=]+$/) { body = body line "\n"; body_lines++ } else { open = 0 }
            }
            END { print count }
          ' "$cert" 2>/dev/null); then
            if test "$block_count" -gt 0; then
              total_files=$((total_files + 1))
              total_blocks=$((total_blocks + block_count))
            else
              echo "Warning: $cert contains no complete PEM block \"-----BEGIN CERTIFICATE-----\" (other labels, such as TRUSTED CERTIFICATE, and indented markers are not accepted); skipping it"
            fi
          else
            cert_failed=1
            break
          fi
          ;;
        *)
          echo "Skipping non-certificate file: $cert"
          ;;
      esac
    done

    if test "$cert_failed" = "1"; then
      rm -rf "$cert_dir"
      echo "Warning: cannot write the certificates under /tmp; certificates in /app/certs are not used"
    elif test "$total_blocks" = "0"; then
      rm -rf "$cert_dir"
      echo "No usable certificates in /app/certs; using the system CA certificates only"
    else
      if test -n "$SSL_CERT_DIR"; then
        echo "SSL_CERT_DIR is set; adding the certificates from /app/certs after $SSL_CERT_DIR"
        SSL_CERT_DIR="$SSL_CERT_DIR:$cert_dir"
      else
        SSL_CERT_DIR="/etc/ssl/certs:$cert_dir"
      fi

      export SSL_CERT_DIR
      echo "Added $total_blocks certificate(s) from $total_files file(s) in /app/certs; SSL_CERT_DIR=$SSL_CERT_DIR"
    fi
  else
    echo "Warning: cannot write the certificates under /tmp; certificates in /app/certs are not used"
  fi
fi

# Start the application
exec dotnet DockerUpdateGuard.dll "$@"
#!/bin/bash
# SessionStart hook for Claude Code on the web (.NET profile): prepares the toolchain so the squad can
# format, build, test and check coverage. Idempotent; does nothing outside remote sessions.
set -euo pipefail

if [[ "${CLAUDE_CODE_REMOTE:-}" != "true" ]]; then
  exit 0
fi

# Linters for the analyzer gate (shellcheck, actionlint, hadolint), pinned and checksum-verified; never fails the session.
bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/install-linters.sh"

cd "${CLAUDE_PROJECT_DIR:-$(pwd)}"

# .NET tools (reihitsu-format) need DOTNET_ROOT when dotnet is not installed in a default location.
dotnet_root="$(dirname "$(readlink -f "$(command -v dotnet)")")"
export DOTNET_ROOT="$dotnet_root"
export PATH="$PATH:$HOME/.dotnet/tools"
if [[ -n "${CLAUDE_ENV_FILE:-}" ]]; then
  {
    echo "export DOTNET_ROOT=\"$dotnet_root\""
    echo "export PATH=\"\$PATH:\$HOME/.dotnet/tools\""
  } >> "$CLAUDE_ENV_FILE"
fi

# The SDK global.json asks for must be installed here: with rollForward latestFeature, that version or a later one of
# the same major and minor. A cloud environment set up for an older SDK installs it in its setup script. A warning
# only; the restore below then names the missing SDK itself.
if [[ -f global.json ]]; then
  wanted="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' global.json \
    | head -n 1)"
  if [[ -n "$wanted" ]]; then
    channel="${wanted%.*}"
    newest="$( (dotnet --list-sdks 2>/dev/null || true) | awk '{print $1}' | grep "^${channel//./\\.}\." \
      | sort -V | tail -n 1 || true)"
    if [[ -z "$newest" ]] || [[ "$(printf '%s\n%s\n' "$wanted" "$newest" | sort -V | head -n 1)" != "$wanted" ]]; then
      echo "global.json asks for .NET SDK ${wanted} or a later one of ${channel}, which this session lacks" \
        "(newest: ${newest:-none}): install it in the environment's setup script" \
        "(curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel ${channel})" >&2
    fi
  fi
fi

# The runtime of every target framework must be installed too: SDK 11 builds a net10.0 project, but its tests run on
# the .NET 10 runtime. A warning only.
runtimes="$(dotnet --list-runtimes 2>/dev/null || true)"
targets="$(grep -rhoE --include='*.csproj' --include='*.props' '<TargetFrameworks?>[^<]+' . 2>/dev/null \
  | grep -oE 'net[0-9]+\.0' | sort -u || true)"
for target in $targets; do
  major="${target#net}"
  major="${major%%.*}"
  if ! grep -q "^Microsoft.NETCore.App ${major}\." <<< "$runtimes"; then
    echo "a project targets $target, but this session has no .NET $major runtime: install it in the environment's" \
      "setup script (curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel ${major}.0)" >&2
  fi
done

# Local tools of the repository's manifest: the formatter (*Format* in .squad/stack.md) and dotnet-sonarscanner
# (*Sonar gate*), both pinned there.
if [[ -e dotnet-tools.json || -e .config/dotnet-tools.json ]]; then
  dotnet tool restore || echo "dotnet tool restore failed; run it before Format and the Sonar gate" >&2
else
  echo "no dotnet tool manifest (.config/dotnet-tools.json); the formatter is not installed" >&2
fi

# Restore every solution at the repository root (the squad's settings name the one the gates build).
for solution in *.slnx *.sln; do
  if [[ -e "$solution" ]]; then
    dotnet restore "$solution"
  fi
done

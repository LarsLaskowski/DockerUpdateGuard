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

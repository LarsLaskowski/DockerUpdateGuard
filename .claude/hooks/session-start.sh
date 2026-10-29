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

# .NET global tools (reihitsu-format) need DOTNET_ROOT when dotnet is not installed in a default location.
dotnet_root="$(dirname "$(readlink -f "$(command -v dotnet)")")"
export DOTNET_ROOT="$dotnet_root"
export PATH="$PATH:$HOME/.dotnet/tools"
if [[ -n "${CLAUDE_ENV_FILE:-}" ]]; then
  {
    echo "export DOTNET_ROOT=\"$dotnet_root\""
    echo "export PATH=\"\$PATH:\$HOME/.dotnet/tools\""
  } >> "$CLAUDE_ENV_FILE"
fi

# Formatter (*Format* in .squad/stack.md), run by the squad member that changed the files
if ! command -v reihitsu-format >/dev/null 2>&1; then
  dotnet tool install -g Reihitsu.Cli
fi

# Scanner of the *Sonar gate* in .squad/stack.md; the gate reports NOT CHECKED when it is missing, so a failed install does not fail the session.
if ! command -v dotnet-sonarscanner >/dev/null 2>&1; then
  dotnet tool install -g dotnet-sonarscanner || echo "dotnet-sonarscanner could not be installed; the Sonar gate will report NOT CHECKED" >&2
fi

# Restore every solution at the repository root (the squad's settings name the one the gates build).
for solution in *.slnx *.sln; do
  if [[ -e "$solution" ]]; then
    dotnet restore "$solution"
  fi
done

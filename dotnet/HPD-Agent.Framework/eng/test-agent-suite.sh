#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOLUTION="$ROOT/HPD-Agent.slnx"
TARGET_FRAMEWORK="${1:-net10.0}"
shift || true
CONFIGURATION="${CONFIGURATION:-Release}"

case "$TARGET_FRAMEWORK" in
  net8.0|net9.0|net10.0) ;;
  *)
    echo "Unsupported target framework: $TARGET_FRAMEWORK" >&2
    exit 2
    ;;
esac

TEST_PROJECTS=()
while IFS= read -r project; do
  TEST_PROJECTS+=("$project")
done < <(
  dotnet sln "$SOLUTION" list |
    tr -d '\r' |
    sed -n '/\.Tests\.csproj$/p'
)

if [[ "${#TEST_PROJECTS[@]}" -eq 0 ]]; then
  echo "No test projects were found in $SOLUTION" >&2
  exit 1
fi

ran=0
for relative_project in "${TEST_PROJECTS[@]}"; do
  if [[ "$relative_project" == *"/HPD-Environment.Virtualization.Apple.Tests/"* && "$(uname -s)" != "Darwin" ]]; then
    echo "Skipping macOS-only test project $relative_project"
    continue
  fi

  project="$ROOT/$relative_project"
  if [[ ! -f "$project" ]]; then
    project="$ROOT/../${relative_project#../}"
  fi

  target_frameworks="$(
    perl -0777 -ne '
      if (m#<TargetFrameworks>([^<]+)</TargetFrameworks>#) { print $1 }
      elsif (m#<TargetFramework>([^<]+)</TargetFramework>#) { print $1 }
    ' "$project"
  )"
  target_frameworks="${target_frameworks:-net10.0;net9.0;net8.0}"

  if [[ ";$target_frameworks;" != *";$TARGET_FRAMEWORK;"* ]]; then
    continue
  fi

  echo "Testing $relative_project ($TARGET_FRAMEWORK)"
  dotnet test "$project" \
    --configuration "$CONFIGURATION" \
    --framework "$TARGET_FRAMEWORK" \
    --no-build \
    --nologo \
    --verbosity minimal \
    "$@"
  ran=$((ran + 1))
done

if [[ "$ran" -eq 0 ]]; then
  echo "No tests support $TARGET_FRAMEWORK" >&2
  exit 1
fi

echo "Completed $ran test projects for $TARGET_FRAMEWORK."

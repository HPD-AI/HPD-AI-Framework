#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../../../.." && pwd)"
framework_root="$repo_root/HPD-AI-Framework/dotnet/HPD-Agent.Framework"
default_env_root="$HOME/.hpd/applevz/images"
default_test_project="$framework_root/test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/HPD-Execution.AppleVirtualization.Tests.csproj"
default_filter="FullyQualifiedName~Real_container_smoke_acceptance_observes_real_engine_status_only_with_explicit_env"

env_root="$default_env_root"
test_project="$default_test_project"
test_filter="$default_filter"
configuration=""
keep_going="false"
skip_prereqs="false"
preserve_serial_log="false"
dry_run="false"
env_files=()

usage() {
  cat <<'USAGE'
Usage:
  run-real-container-acceptance-matrix.sh [options]

Runs the opt-in Apple Virtualization real container acceptance test once per
prepared hpd-applevz-real.env file. Runs are sequential because each one boots a
real VM.

Options:
  --env-file PATH             add one env file to the matrix; repeatable
  --env-root PATH             discover */hpd-applevz-real.env under PATH
                              default: ~/.hpd/applevz/images
  --test-project PATH         default: AppleVirtualization test project
  --filter TEXT               default: real container acceptance test
  --configuration NAME        optional dotnet test configuration
  --skip-prereqs              do not run check-real-acceptance-prereqs.sh
  --preserve-serial-log       do not remove HPD_APPLEVZ_GUEST_SERIAL_LOG first
  --keep-going                continue after a failed env and summarize failures
  --dry-run                   print selected env files and metadata only
  -h, --help
USAGE
}

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --env-file) env_files+=("$2"); shift 2 ;;
    --env-root) env_root="$2"; shift 2 ;;
    --test-project) test_project="$2"; shift 2 ;;
    --filter) test_filter="$2"; shift 2 ;;
    --configuration) configuration="$2"; shift 2 ;;
    --skip-prereqs) skip_prereqs="true"; shift ;;
    --preserve-serial-log) preserve_serial_log="true"; shift ;;
    --keep-going) keep_going="true"; shift ;;
    --dry-run) dry_run="true"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'unknown argument: %s\n\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ "${#env_files[@]}" -eq 0 ]]; then
  if [[ -d "$env_root" ]]; then
    while IFS= read -r env_file; do
      env_files+=("$env_file")
    done < <(find "$env_root" -mindepth 2 -maxdepth 2 -name hpd-applevz-real.env -type f | sort)
  fi
fi

if [[ "${#env_files[@]}" -eq 0 ]]; then
  printf 'no hpd-applevz-real.env files found under %s\n' "$env_root" >&2
  exit 1
fi

if [[ ! -f "$test_project" ]]; then
  printf 'test project not found: %s\n' "$test_project" >&2
  exit 1
fi

check_script="$framework_root/docs/apple-virtualization/scripts/check-real-acceptance-prereqs.sh"
if [[ "$skip_prereqs" != "true" && ! -x "$check_script" ]]; then
  printf 'prereq script is not executable: %s\n' "$check_script" >&2
  exit 1
fi

filter_noise() {
  if command -v rg >/dev/null 2>&1; then
    rg -v 'warning|Warning\(s\)|^\s*$|Determining projects to restore|All projects are up-to-date| -> |CSSM_ModuleLoad|Skipping project' || true
  else
    cat
  fi
}

read_env_value() {
  local env_file="$1"
  local name="$2"
  (
    set -a
    # shellcheck disable=SC1090
    . "$env_file"
    set +a
    printf '%s' "${!name:-}"
  )
}

run_dotnet_test() {
  local env_file="$1"
  local output
  local status
  local args=(test "$test_project" -f net10.0 --filter "$test_filter" -v minimal)
  if [[ -n "$configuration" ]]; then
    args+=(-c "$configuration")
  fi

  set +e
  output="$(
    set -a
    # shellcheck disable=SC1090
    . "$env_file"
    set +a
    dotnet "${args[@]}" 2>&1
  )"
  status="$?"
  set -e

  printf '%s\n' "$output" | filter_noise
  return "$status"
}

failures=0
completed=0

printf 'Apple VZ real container acceptance matrix\n'
printf 'test project: %s\n' "$test_project"
printf 'test filter:  %s\n' "$test_filter"
printf 'env count:    %d\n' "${#env_files[@]}"

for env_file in "${env_files[@]}"; do
  if [[ ! -f "$env_file" ]]; then
    printf '\n[missing] %s\n' "$env_file" >&2
    failures=$((failures + 1))
    if [[ "$keep_going" != "true" ]]; then
      exit 1
    fi
    continue
  fi

  engine_kind="$(read_env_value "$env_file" HPD_APPLEVZ_CONTAINER_ENGINE_KIND)"
  engine_api="$(read_env_value "$env_file" HPD_APPLEVZ_CONTAINER_ENGINE_API)"
  authority_mode="$(read_env_value "$env_file" HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE)"
  socket_path="$(read_env_value "$env_file" HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH)"
  serial_log="$(read_env_value "$env_file" HPD_APPLEVZ_GUEST_SERIAL_LOG)"

  printf '\n=== %s ===\n' "$env_file"
  printf 'engine: %s / %s / %s\n' "${engine_kind:-<unset>}" "${engine_api:-<unset>}" "${authority_mode:-<unset>}"
  printf 'socket: %s\n' "${socket_path:-<unset>}"

  if [[ "$dry_run" == "true" ]]; then
    continue
  fi

  if [[ "$skip_prereqs" != "true" ]]; then
    "$check_script" "$env_file"
  fi

  if [[ "$preserve_serial_log" != "true" && -n "$serial_log" ]]; then
    rm -f "$serial_log"
  fi

  if run_dotnet_test "$env_file"; then
    printf '[pass] %s\n' "$env_file"
    completed=$((completed + 1))
  else
    printf '[fail] %s\n' "$env_file" >&2
    failures=$((failures + 1))
    if [[ "$keep_going" != "true" ]]; then
      exit 1
    fi
  fi
done

if [[ "$dry_run" == "true" ]]; then
  printf '\ndry run complete\n'
  exit 0
fi

printf '\nmatrix complete: %d passed, %d failed\n' "$completed" "$failures"
if [[ "$failures" -ne 0 ]]; then
  exit 1
fi

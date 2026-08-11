#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$root/test/HPD.Gateway.Admission.MultiProcess/HPD.Gateway.Admission.MultiProcess.csproj"
configuration="${CONFIGURATION:-Release}"
base_port="${HPD_GATEWAY_ADMISSION_BASE_PORT:-56300}"
redis_port="${HPD_GATEWAY_ADMISSION_REDIS_PORT:-6394}"
redis_server="${VALKEY_SERVER:-$(command -v valkey-server || command -v redis-server)}"
redis_cli="${VALKEY_CLI:-$(command -v valkey-cli || command -v redis-cli)}"
work="$(mktemp -d "${TMPDIR:-/tmp}/hpd-gateway-admission.XXXXXX")"
publish="$work/publish"
server_pid=""

cleanup() {
  if [[ -n "$server_pid" ]]; then
    "$redis_cli" -p "$redis_port" shutdown nosave >/dev/null 2>&1 || true
    wait "$server_pid" 2>/dev/null || true
  fi
  find "$work" -type f -delete 2>/dev/null || true
  find "$work" -depth -type d -empty -delete 2>/dev/null || true
}
trap cleanup EXIT

"$redis_server" --port "$redis_port" --save '' --appendonly no --dir "$work" >"$work/redis.log" 2>&1 &
server_pid="$!"
for _ in {1..100}; do
  if "$redis_cli" -p "$redis_port" ping 2>/dev/null | grep -q PONG; then break; fi
  sleep 0.05
done
"$redis_cli" -p "$redis_port" ping | grep -q PONG

dotnet build "$project" -c "$configuration" --nologo
assembly="$root/test/HPD.Gateway.Admission.MultiProcess/bin/$configuration/net10.0/HPD.Gateway.Admission.MultiProcess.dll"
redis="127.0.0.1:$redis_port,abortConnect=false,connectTimeout=1000,syncTimeout=1000"

run_case() {
  local offset="$1" scenario="$2" distribution="$3" algorithm="$4" limit="$5" replicas="${6:-3}"
  dotnet "$assembly" controller --assembly "$assembly" --redis "$redis" \
    --base-port "$((base_port + offset))" --replicas "$replicas" --distribution "$distribution" \
    --scenario "$scenario" --authority fleet-evidence \
    --key-prefix "hpd:evidence:$scenario:$distribution:$algorithm" --algorithm "$algorithm" --limit "$limit"
}

run_case 0 quota round-robin fixed 400 4
run_case 100 quota sticky fixed 400 4
run_case 200 quota uneven fixed 400
run_case 300 scale round-robin fixed 400
run_case 350 scale-in uneven fixed 400 4
run_case 400 restart uneven fixed 400
run_case 500 concurrency round-robin fixed 1000
run_case 600 quota round-robin sliding 50
run_case 700 quota round-robin token 50
run_case 750 race round-robin fixed 100 4

dotnet "$assembly" controller --assembly "$assembly" \
  --redis "127.0.0.1:$((redis_port + 1)),abortConnect=false,connectTimeout=50" \
  --base-port "$((base_port + 800))" --replicas 2 --distribution round-robin \
  --scenario unavailable --authority fleet-evidence --key-prefix hpd:evidence:unavailable \
  --algorithm fixed --limit 10 --failure reject
for disposition in bypass fallback; do
  dotnet "$assembly" controller --assembly "$assembly" \
    --redis "127.0.0.1:$((redis_port + 1)),abortConnect=false,connectTimeout=50" \
    --base-port "$((base_port + 820 + (${#disposition} * 2)))" --replicas 2 --distribution round-robin \
    --scenario unavailable --authority fleet-evidence --key-prefix "hpd:evidence:unavailable:$disposition" \
    --algorithm fixed --limit 10 --failure "$disposition"
done

for topology in CLUSTER SENTINEL; do
  variable="HPD_GATEWAY_REDIS_${topology}"
  endpoint="${!variable:-}"
  if [[ -n "$endpoint" ]]; then
    dotnet "$assembly" controller --assembly "$assembly" --redis "$endpoint" \
      --base-port "$((base_port + 900))" --replicas 3 --distribution uneven \
      --scenario quota --authority "fleet-${topology,,}" \
      --key-prefix "hpd:evidence:${topology,,}" --algorithm fixed --limit 400
  fi
done

if [[ "${HPD_GATEWAY_ADMISSION_AOT:-1}" == "1" ]]; then
  dotnet publish "$project" -c "$configuration" -r osx-arm64 -p:PublishAot=true -o "$publish"
  "$publish/HPD.Gateway.Admission.MultiProcess" controller \
    --assembly "$publish/HPD.Gateway.Admission.MultiProcess" --redis "$redis" \
    --base-port "$((base_port + 1000))" --replicas 4 --distribution uneven \
    --scenario scale-in --authority fleet-native --key-prefix hpd:evidence:native \
    --algorithm fixed --limit 100
fi

echo "HPD Gateway Slice 6 multi-process evidence passed."

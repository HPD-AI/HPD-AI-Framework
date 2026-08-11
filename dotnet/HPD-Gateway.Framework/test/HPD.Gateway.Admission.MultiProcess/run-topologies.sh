#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$root/test/HPD.Gateway.Admission.MultiProcess/HPD.Gateway.Admission.MultiProcess.csproj"
configuration="${CONFIGURATION:-Release}"
base_port="${HPD_GATEWAY_ADMISSION_TOPOLOGY_BASE_PORT:-60100}"
cluster_port="${HPD_GATEWAY_ADMISSION_CLUSTER_PORT:-6400}"
primary_port="${HPD_GATEWAY_ADMISSION_PRIMARY_PORT:-6410}"
sentinel_port="${HPD_GATEWAY_ADMISSION_SENTINEL_PORT:-26410}"
server="${VALKEY_SERVER:-$(command -v valkey-server || command -v redis-server)}"
cli="${VALKEY_CLI:-$(command -v valkey-cli || command -v redis-cli)}"
work="$(mktemp -d "${TMPDIR:-/tmp}/hpd-gateway-admission-topology.XXXXXX")"
pids=()

cleanup() {
  for pid in "${pids[@]:-}"; do kill "$pid" >/dev/null 2>&1 || true; done
  for pid in "${pids[@]:-}"; do wait "$pid" 2>/dev/null || true; done
  find "$work" -type f -delete 2>/dev/null || true
  find "$work" -depth -type d -empty -delete 2>/dev/null || true
}
trap cleanup EXIT

wait_ping() {
  local port="$1"
  for _ in {1..200}; do
    if "$cli" -p "$port" ping 2>/dev/null | grep -q PONG; then return; fi
    sleep 0.05
  done
  echo "Redis-compatible server on port $port did not start." >&2
  exit 1
}

start_server() {
  local port="$1" dir="$2"; shift 2
  mkdir -p "$dir"
  "$server" --port "$port" --save '' --appendonly no --dir "$dir" "$@" >"$dir/server.log" 2>&1 &
  pids+=("$!")
  wait_ping "$port"
}

run_controller() {
  local endpoint="$1" port="$2" prefix="$3" limit="${4:-100}"
  dotnet "$assembly" controller --assembly "$assembly" --redis "$endpoint" \
    --base-port "$port" --replicas 3 --distribution uneven --scenario quota \
    --authority fleet-topology --key-prefix "$prefix" --algorithm fixed --limit "$limit"
}

dotnet build "$project" -c "$configuration" --nologo
assembly="$root/test/HPD.Gateway.Admission.MultiProcess/bin/$configuration/net10.0/HPD.Gateway.Admission.MultiProcess.dll"

# Three independent masters prove Cluster routing, redirection, and partition-slot distribution.
cluster_endpoints=()
for index in 0 1 2; do
  port="$((cluster_port + index))"
  start_server "$port" "$work/cluster-$index" --cluster-enabled yes \
    --cluster-config-file nodes.conf --cluster-node-timeout 1000
  cluster_endpoints+=("127.0.0.1:$port")
done
"$cli" --cluster create "${cluster_endpoints[@]}" --cluster-replicas 0 --cluster-yes >/dev/null
cluster_configuration="$(IFS=,; echo "${cluster_endpoints[*]}"),abortConnect=false,connectTimeout=1000,syncTimeout=1000"
run_controller "$cluster_configuration" "$base_port" hpd:evidence:cluster 400

# Move the exact live quota slot to another master and prove retained state follows it.
source_port=""
key=""
for index in 0 1 2; do
  candidate="$($cli -c -p "$((cluster_port + index))" --scan --pattern 'hpd:evidence:cluster:*' | head -1)"
  if [[ -n "$candidate" ]]; then source_port="$((cluster_port + index))"; key="$candidate"; break; fi
done
[[ -n "$source_port" && -n "$key" ]]
slot="$($cli -c -p "$source_port" cluster keyslot "$key")"
source_id="$($cli -p "$source_port" cluster myid)"
target_port="$((source_port == cluster_port ? cluster_port + 1 : cluster_port))"
target_id="$($cli -p "$target_port" cluster myid)"
"$cli" -p "$target_port" cluster setslot "$slot" importing "$source_id" >/dev/null
"$cli" -p "$source_port" cluster setslot "$slot" migrating "$target_id" >/dev/null
while read -r moving_key; do
  [[ -z "$moving_key" ]] && continue
  "$cli" -p "$source_port" migrate 127.0.0.1 "$target_port" "$moving_key" 0 5000 >/dev/null
done < <("$cli" -p "$source_port" cluster getkeysinslot "$slot" 1000)
for index in 0 1 2; do "$cli" -p "$((cluster_port + index))" cluster setslot "$slot" node "$target_id" >/dev/null; done
dotnet "$assembly" controller --assembly "$assembly" --redis "$cluster_configuration" \
  --base-port "$((base_port + 50))" --replicas 2 --distribution round-robin --scenario exhausted \
  --authority fleet-topology --key-prefix hpd:evidence:cluster --algorithm fixed --limit 400

# Evict scripts from every Cluster node. The next real fleet operation must recover transparently.
for index in 0 1 2; do "$cli" -c -p "$((cluster_port + index))" script flush >/dev/null; done
run_controller "$cluster_configuration" "$((base_port + 100))" hpd:evidence:cluster-flush 100

# Standalone primary/replica plus three Sentinels. Quorum is two.
start_server "$primary_port" "$work/primary"
start_server "$((primary_port + 1))" "$work/replica" --replicaof 127.0.0.1 "$primary_port"
for _ in {1..300}; do
  if "$cli" -p "$primary_port" info replication 2>/dev/null | grep -q 'connected_slaves:1'; then break; fi
  sleep 0.05
done
"$cli" -p "$primary_port" info replication | grep -q 'connected_slaves:1'
for index in 0 1 2; do
  port="$((sentinel_port + index))"
  directory="$work/sentinel-$index"
  mkdir -p "$directory"
  config="$directory/sentinel.conf"
  printf '%s\n' \
    "port $port" \
    "dir $directory" \
    "sentinel monitor hpdmaster 127.0.0.1 $primary_port 2" \
    "sentinel down-after-milliseconds hpdmaster 1000" \
    "sentinel failover-timeout hpdmaster 5000" \
    "sentinel parallel-syncs hpdmaster 1" >"$config"
  "$server" "$config" --sentinel >"$directory/server.log" 2>&1 &
  pids+=("$!")
  wait_ping "$port"
done
sentinel_configuration="127.0.0.1:$sentinel_port,127.0.0.1:$((sentinel_port + 1)),127.0.0.1:$((sentinel_port + 2)),serviceName=hpdmaster,abortConnect=false,connectTimeout=1000,syncTimeout=1000"
run_controller "$sentinel_configuration" "$((base_port + 200))" hpd:evidence:sentinel-before 100
sentinel_key="$($cli -p "$primary_port" --scan --pattern 'hpd:evidence:sentinel-before:*' | head -1)"
[[ -n "$sentinel_key" ]]
for _ in {1..300}; do
  primary_state="$($cli --raw -p "$primary_port" hgetall "$sentinel_key" | shasum -a 256 | cut -d' ' -f1)"
  replica_state="$($cli --raw -p "$((primary_port + 1))" hgetall "$sentinel_key" | shasum -a 256 | cut -d' ' -f1)"
  if [[ "$($cli -p "$((primary_port + 1))" exists "$sentinel_key")" == "1" && "$primary_state" == "$replica_state" ]]; then break; fi
  sleep 0.05
done
[[ "$($cli -p "$((primary_port + 1))" exists "$sentinel_key")" == "1" ]]
[[ "$($cli --raw -p "$primary_port" hgetall "$sentinel_key" | shasum -a 256 | cut -d' ' -f1)" == \
   "$($cli --raw -p "$((primary_port + 1))" hgetall "$sentinel_key" | shasum -a 256 | cut -d' ' -f1)" ]]
"$cli" -p "$sentinel_port" sentinel failover hpdmaster >/dev/null
for _ in {1..300}; do
  current="$($cli -p "$sentinel_port" sentinel get-master-addr-by-name hpdmaster 2>/dev/null | tail -1)"
  if [[ "$current" == "$((primary_port + 1))" ]]; then break; fi
  sleep 0.1
done
current="$($cli -p "$sentinel_port" sentinel get-master-addr-by-name hpdmaster | tail -1)"
[[ "$current" == "$((primary_port + 1))" ]]
dotnet "$assembly" controller --assembly "$assembly" --redis "$sentinel_configuration" \
  --base-port "$((base_port + 250))" --replicas 2 --distribution round-robin --scenario exhausted \
  --authority fleet-topology --key-prefix hpd:evidence:sentinel-before --algorithm fixed --limit 100
run_controller "$sentinel_configuration" "$((base_port + 300))" hpd:evidence:sentinel-after 100

# Same-process connection loss and recovery against a restartable standalone authority.
recovery_port="$((primary_port + 10))"
recovery_dir="$work/recovery"
start_server "$recovery_port" "$recovery_dir"
control="$work/control"
mkdir -p "$control"
dotnet "$assembly" controller --assembly "$assembly" \
  --redis "127.0.0.1:$recovery_port,abortConnect=false,connectTimeout=100,syncTimeout=100" \
  --base-port "$((base_port + 400))" --replicas 1 --distribution sticky --scenario recovery \
  --control-dir "$control" --authority fleet-recovery --key-prefix hpd:evidence:recovery \
  --algorithm fixed --limit 100 >"$work/recovery-controller.log" 2>&1 &
controller_pid="$!"
pids+=("$controller_pid")
for _ in {1..200}; do [[ -f "$control/ready" ]] && break; sleep 0.05; done
[[ -f "$control/ready" ]]
"$cli" -p "$recovery_port" shutdown nosave >/dev/null
touch "$control/outage"
for _ in {1..300}; do [[ -f "$control/outage-observed" ]] && break; sleep 0.05; done
[[ -f "$control/outage-observed" ]]
start_server "$recovery_port" "$recovery_dir"
touch "$control/recovered"
wait "$controller_pid"

echo "HPD Gateway Slice 6 Redis Cluster, Sentinel failover, script-cache loss, and recovery evidence passed."

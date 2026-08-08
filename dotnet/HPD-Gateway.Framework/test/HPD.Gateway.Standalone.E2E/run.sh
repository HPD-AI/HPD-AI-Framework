#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
gateway_root="$(cd "$script_dir/../.." && pwd)"
dotnet_root="$(cd "$gateway_root/.." && pwd)"
runtime_id="${HPD_GATEWAY_E2E_RID:-osx-arm64}"
work="$(mktemp -d "${TMPDIR:-/tmp}/hpd-gateway-standalone-e2e.XXXXXX")"
gateway_pid=""
backend_pid=""

cleanup() {
  if [[ -n "$gateway_pid" ]]; then kill -TERM "$gateway_pid" 2>/dev/null || true; wait "$gateway_pid" 2>/dev/null || true; fi
  if [[ -n "$backend_pid" ]]; then kill -TERM "$backend_pid" 2>/dev/null || true; wait "$backend_pid" 2>/dev/null || true; fi
  find "$work" -depth -delete 2>/dev/null || true
}
trap cleanup EXIT INT TERM

for command in dotnet openssl python3 curl jq; do
  command -v "$command" >/dev/null || { echo "missing required command: $command" >&2; exit 2; }
done

read -r backend_port data_port management_port < <(python3 - <<'PY'
import socket
sockets=[]
ports=[]
for _ in range(3):
    sock=socket.socket()
    sock.bind(("127.0.0.1", 0))
    sockets.append(sock)
    ports.append(sock.getsockname()[1])
print(*ports)
for sock in sockets: sock.close()
PY
)

publish="$work/publish"
dotnet publish "$gateway_root/src/HPD.Gateway.Standalone/HPD.Gateway.Standalone.csproj" \
  -c Release -r "$runtime_id" -p:PublishAot=true -p:StripSymbols=true -m:1 -o "$publish"
binary="$publish/HPD.Gateway.Standalone"
test -x "$binary"

openssl req -x509 -newkey rsa:2048 -sha256 -nodes \
  -keyout "$work/key.pem" -out "$work/cert.pem" -days 2 -subj '/CN=localhost' \
  -addext 'subjectAltName=DNS:localhost' -addext 'extendedKeyUsage=serverAuth' \
  -addext 'keyUsage=digitalSignature,keyEncipherment' >/dev/null 2>&1
openssl pkcs12 -export -out "$work/server.pfx" -inkey "$work/key.pem" -in "$work/cert.pem" \
  -passout pass:evidence >/dev/null 2>&1

cat > "$work/host.json" <<JSON
{
  "schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"hostId":{"value":"standalone"},
  "dataListeners":[{"id":{"value":"https"},"binding":"loopback","port":$data_port,"protocols":"http1",
    "tls":{"fallback":"rejectUnmatchedOrMissingSni","sni":[{"hostnamePattern":"localhost",
      "certificate":{"provider":{"value":"pfx"},"name":{"value":"localhost"},"version":"v1"}}]}}],
  "managementListeners":[{"id":{"value":"management"},"binding":"loopback","port":$management_port,
    "protocols":"http1","exposure":"loopbackDevelopment","allowDevelopmentCleartext":true,
    "endpointSurfaceId":"gateway-admin-v1"}]
}
JSON

cat > "$work/gateway.json" <<JSON
{
  "schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,
  "metadata":{"labels":[],"annotations":[]},
  "routes":[{"id":{"value":"route"},"enabled":true,"listener":{"value":"https"},
    "match":{"methods":[],"hosts":["localhost"],"path":"/{**catchall}","headers":[],"query":[]},
    "upstream":{"value":"backend"},"declarations":{},"metadata":{"labels":[],"annotations":[]}}],
  "upstreams":[{"id":{"value":"backend"},"endpoints":{"kind":"static","destinations":[
    {"id":{"value":"one"},"address":"http://127.0.0.1:$backend_port/","metadata":{"labels":[],"annotations":[]}}]},
    "loadBalancing":{"kind":"powerOfTwoChoices"},
    "transport":{"useProxy":false,"enableMultipleHttp2Connections":false,"requestHeaderEncodingLatin1":false},
    "request":{"version":"http2","versionSelection":"requestVersionOrLower","allowResponseBuffering":false},
    "metadata":{"labels":[],"annotations":[]} }],
  "definitions":{"authorization":[],"cors":[],"trafficAdmission":[],"requestTimeout":[],"outputCache":[],
    "telemetry":[],"inspection":[],"credentialDisposition":[]},"rootDefaults":{}
}
JSON

cat > "$work/bootstrap.json" <<JSON
{
  "schemaVersion":"hpd.gateway.standalone/v2","hostConfigurationPath":"$work/host.json",
  "gatewayConfigurationPath":"$work/gateway.json","namespaceId":"namespace-a","targetNodeId":"node-a",
  "candidateId":{"value":"standalone-initial"},"authorityId":"standalone-authority",
  "authorityEpoch":"standalone-epoch","authorityVersion":1,
  "management":{"databasePath":"$work/management.db","managementAuthorityId":"management-authority",
    "planProtectionKeyHex":"1111111111111111111111111111111111111111111111111111111111111111",
    "tokenProtectionKeyHex":"2222222222222222222222222222222222222222222222222222222222222222",
    "tokenProtectionIssueNotBeforeUtc":"2020-01-01T00:00:00Z",
    "desiredStateTokenKeyHex":"3333333333333333333333333333333333333333333333333333333333333333",
    "epochReservationKeyHex":"5555555555555555555555555555555555555555555555555555555555555555",
    "jwtAuthority":"https://issuer.example","jwtAudience":"hpd-gateway",
    "jwtSigningKeyHex":"4444444444444444444444444444444444444444444444444444444444444444"},
  "certificates":[{"provider":{"value":"pfx"},"name":{"value":"localhost"},"version":"v1",
    "pfxPath":"$work/server.pfx","passwordEnvironmentVariable":"HPD_GATEWAY_E2E_PFX_PASSWORD"}]
}
JSON

printf 'native-standalone-forwarding-ok\n' > "$work/index.html"
python3 -m http.server "$backend_port" --bind 127.0.0.1 --directory "$work" >"$work/backend.log" 2>&1 &
backend_pid=$!

jwt="$(python3 - <<'PY'
import base64, hashlib, hmac, json, time
encode=lambda value: base64.urlsafe_b64encode(value).rstrip(b'=').decode()
header=encode(json.dumps({"alg":"HS256","typ":"JWT"},separators=(',',':')).encode())
payload=encode(json.dumps({"sub":"actor-a","hpd_namespace":"namespace-a","iss":"https://issuer.example",
 "aud":"hpd-gateway","exp":int(time.time())+900},separators=(',',':')).encode())
signature=encode(hmac.new(bytes.fromhex('44'*32),f'{header}.{payload}'.encode(),hashlib.sha256).digest())
print(f'{header}.{payload}.{signature}')
PY
)"
auth=(-H "Authorization: Bearer $jwt")

start_gateway() {
  HPD_GATEWAY_E2E_PFX_PASSWORD=evidence ASPNETCORE_ENVIRONMENT=Development \
    "$binary" "$work/bootstrap.json" >"$work/gateway.log" 2>&1 &
  gateway_pid=$!
  for _ in $(seq 1 100); do
    if curl -fsS "${auth[@]}" "http://127.0.0.1:$management_port/management/gateway/v1/capabilities" >/dev/null; then return; fi
    kill -0 "$gateway_pid" 2>/dev/null || { cat "$work/gateway.log" >&2; exit 1; }
    sleep 0.1
  done
  cat "$work/gateway.log" >&2
  exit 1
}

stop_gateway() {
  kill -TERM "$gateway_pid"
  wait "$gateway_pid"
  gateway_pid=""
}

expect_status() {
  local expected="$1"; shift
  local actual
  actual="$(curl -ksS -o "$work/response" -w '%{http_code}' "$@")"
  [[ "$actual" == "$expected" ]] || { echo "expected $expected, got $actual" >&2; cat "$work/response" >&2; exit 1; }
}

start_gateway
expect_status 200 "${auth[@]}" "http://127.0.0.1:$management_port/management/gateway/v1/capabilities"
expect_status 404 "${auth[@]}" "http://127.0.0.1:$management_port/management/gateway/v1/namespaces/foreign/targets/node-a/status"
expect_status 404 "${auth[@]}" "https://localhost:$data_port/management/gateway/v1/capabilities"
expect_status 200 "${auth[@]}" "http://127.0.0.1:$management_port/openapi/hpd-gateway-v1.json"
expect_status 200 "https://localhost:$data_port/"
grep -q 'native-standalone-forwarding-ok' "$work/response"

expect_status 201 "${auth[@]}" -H 'Idempotency-Key: provision-node-b' -X POST \
  "http://127.0.0.1:$management_port/management/gateway/v1/namespaces/namespace-a/targets/node-b:provision"
expect_status 200 "${auth[@]}" \
  "http://127.0.0.1:$management_port/management/gateway/v1/namespaces/namespace-a/targets/node-b/status"
jq -e '.nodeObservation == "NotAttempted" and .node == null' "$work/response" >/dev/null

jq -n --rawfile configuration "$work/gateway.json" \
  '{configurationJson:$configuration,sourceKind:"e2e",sourceId:"standalone",description:"native activation"}' > "$work/submit.json"
expect_status 202 "${auth[@]}" -H 'Content-Type: application/json' -H 'Idempotency-Key: native-activation' \
  -H 'X-Correlation-ID: native-correlation' --data-binary "@$work/submit.json" \
  "http://127.0.0.1:$management_port/management/gateway/v1/namespaces/namespace-a/targets/node-a/revisions:submitAndActivate"
revision="$(jq -r .revisionId "$work/response")"

for _ in $(seq 1 150); do
  curl -fsS "${auth[@]}" \
    "http://127.0.0.1:$management_port/management/gateway/v1/namespaces/namespace-a/targets/node-a/activations" > "$work/activations.json"
  jq -e '.outcomes.items[] | select(.kind == "ActiveAcknowledged")' "$work/activations.json" >/dev/null && break
  sleep 0.1
done
jq -e '.outcomes.items[] | select(.kind == "ActiveAcknowledged")' "$work/activations.json" >/dev/null

expect_status 202 "${auth[@]}" -H 'Content-Type: application/json' -H 'Idempotency-Key: native-activation' \
  -H 'X-Correlation-ID: native-correlation' --data-binary "@$work/submit.json" \
  "http://127.0.0.1:$management_port/management/gateway/v1/namespaces/namespace-a/targets/node-a/revisions:submitAndActivate"
jq -e --arg revision "$revision" '.duplicate == true and .revisionId == $revision' "$work/response" >/dev/null

stop_gateway
start_gateway
expect_status 202 "${auth[@]}" -H 'Content-Type: application/json' -H 'Idempotency-Key: native-activation' \
  -H 'X-Correlation-ID: native-correlation' --data-binary "@$work/submit.json" \
  "http://127.0.0.1:$management_port/management/gateway/v1/namespaces/namespace-a/targets/node-a/revisions:submitAndActivate"
jq -e --arg revision "$revision" '.duplicate == true and .revisionId == $revision' "$work/response" >/dev/null
expect_status 200 "https://localhost:$data_port/"
grep -q 'native-standalone-forwarding-ok' "$work/response"
stop_gateway

echo "HPD.Gateway standalone Native AOT E2E passed"

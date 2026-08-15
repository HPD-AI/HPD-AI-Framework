#!/usr/bin/env bash
set -Eeuo pipefail

readonly root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly rid="${1:-$(dotnet --info | awk -F: '/^[[:space:]]*RID:/{gsub(/[[:space:]]/, "", $2); print $2; exit}')}"
readonly supported=" linux-arm64 linux-x64 osx-arm64 osx-x64 win-arm64 win-x64 "
if [[ "${supported}" != *" ${rid} "* ]]; then
  printf 'Unsupported Silero qualification RID: %s\n' "${rid}" >&2
  exit 2
fi

readonly work="$(mktemp -d "${TMPDIR:-/tmp}/hpd-silero-qualification.XXXXXX")"
trap 'rm -rf "${work}"' EXIT

report_failure() {
  local status=$?
  printf 'silero-qualification-%s=fail\n' "${rid}" >&2
  local log
  for log in "${work}"/*.log; do
    [[ -f "${log}" ]] || continue
    grep -E 'error [A-Z]+[0-9]+|FAILED|Failed|Exception|Expected:|Actual:|\[FAIL\]|No space' "${log}" \
      | tail -n 40 >&2 || true
  done
  exit "${status}"
}
trap report_failure ERR
readonly model="$(${root}/eng/fetch-silero-vad-v6.2.sh)"
readonly tests="${root}/test/HPD.Agent.Audio.V2.Tests/HPD.Agent.Audio.V2.Tests.csproj"
readonly provider="${root}/src/HPD-Agent.Providers.Audio/HPD-Agent.Providers.Audio.Silero/HPD-Agent.Providers.Audio.Silero.csproj"
readonly smoke="${root}/test/HPD-Agent.Audio.VoiceActivity.AotSmoke/HPD-Agent.Audio.VoiceActivity.AotSmoke.csproj"

for framework in net8.0 net9.0 net10.0; do
  HPD_SILERO_VAD_MODEL_PATH="${model}" dotnet test "${tests}" -f "${framework}" \
    --filter FullyQualifiedName~SileroAudioProviderV1Tests -v:q \
    >"${work}/test-${framework}.log" 2>&1
  printf 'silero-tests-%s=pass\n' "${framework}"
done

dotnet build "${provider}" -f net10.0 --no-dependencies -warnaserror -v:q \
  >"${work}/warnings-as-errors.log" 2>&1
printf 'silero-warnings-as-errors=pass\n'

dotnet publish "${smoke}" -c Release -r "${rid}" --self-contained true \
  -o "${work}/publish" -v:q >"${work}/aot-publish.log" 2>&1
readonly executable="${work}/publish/HPD-Agent.Audio.VoiceActivity.AotSmoke$([[ "${rid}" == win-* ]] && printf '.exe')"
HPD_SILERO_VAD_MODEL_PATH="${model}" HPD_SILERO_SOAK_WINDOWS=1000000 \
  "${executable}" >"${work}/aot-run.log" 2>&1
grep -qx 'voice-activity-aot=pass' "${work}/aot-run.log"
printf 'silero-native-aot-%s=pass\n' "${rid}"

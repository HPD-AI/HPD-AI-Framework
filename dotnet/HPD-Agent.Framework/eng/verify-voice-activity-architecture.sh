#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
voice="$root/src/HPD-Agent.Audio/Abstractions/VoiceActivity"
silero="$root/src/HPD-Agent.Providers.Audio/HPD-Agent.Providers.Audio.Silero"
agent="$root/src/HPD-Agent"

fail_if_match() {
  local code="$1"
  local pattern="$2"
  shift 2
  local output
  if output="$(grep -R -n -E --include='*.cs' "$pattern" "$@" 2>/dev/null)"; then
    printf 'voice-activity-architecture=%s\n%s\n' "$code" "$output" >&2
    exit 1
  fi
}

fail_if_match legacy-live-contract \
  'IVoiceActivityDetector|(^|[^[:alnum:]_])Vad(Event|Result|State)([^[:alnum:]_]|$)|VoiceActivityEvidenceDetail|TurnEvidence.*VoiceActivity' \
  "$root/src"
fail_if_match duplicate-authority \
  'VadProviderRegistry|DetectorRegistry|VadScheduler|VadClock|VoiceActivityProviderRegistry|VoiceActivityScheduler' \
  "$root/src"
fail_if_match hidden-work \
  'new[[:space:]]+Thread|Task\.Run|Channel\.Create|ConcurrentQueue' "$voice" "$silero"
fail_if_match ambient-time \
  'DateTime\.Now|DateTime\.UtcNow|Stopwatch\.GetTimestamp' "$voice" "$silero"
fail_if_match runtime-reflection \
  'Assembly\.Load|GetTypes\(|Activator\.CreateInstance' "$voice" "$silero"
fail_if_match public-media-owner \
  'public[^\n]*(MemoryOwner|IMemoryOwner|AudioFrameView|OwnedAudioFrame)' "$voice"
fail_if_match adjacent-authority-call \
  'CreateResponse|Interrupt(Output)?|CancelOutput|CommitSemantic|CommitEndpoint|MutateRoute|AppendAgentInput' \
  "$voice/VoiceActivityPromotionV1.cs"

expected_events="$voice/VoiceActivityStatusProjectionV1.cs"
events_files="$(grep -R -l -E --include='*.cs' 'HPD\.Events' "$voice" | sort)"
events_count="$(printf '%s\n' "$events_files" | awk 'NF { count++ } END { print count + 0 }')"
if [[ "$events_count" != 1 || "$events_files" != "$expected_events" ]]; then
  printf 'voice-activity-architecture=hpd-events-authority\n%s\n' "${events_files:-none}" >&2
  exit 1
fi

compiler_count="$(grep -R -n -E --include='*.cs' 'internal static class VoiceActivityPlanCompilerV1' "$voice" | wc -l | tr -d ' ')"
promoter_count="$(grep -R -n -E --include='*.cs' 'internal sealed class VoiceActivityPromoterV1' "$voice" | wc -l | tr -d ' ')"
if [[ "$compiler_count" != 1 || "$promoter_count" != 1 ]]; then
  printf 'voice-activity-architecture=writer-count compiler=%s promoter=%s\n' \
    "$compiler_count" "$promoter_count" >&2
  exit 1
fi

if grep -n -E '<ProjectReference[^>]+HPD-Agent\.Audio' "$agent/HPD-Agent.csproj" >/dev/null; then
  printf 'voice-activity-architecture=reverse-audio-dependency\n' >&2
  exit 1
fi

fail_if_match compatibility-surface \
  'namespace[[:space:]].*(LegacyVoiceActivity|VoiceActivityLegacy)|class[[:space:]].*(LegacyVad|VadCompatibility)' \
  "$root/src"

printf 'voice-activity-architecture=pass compiler=%s promoter=%s events=diagnostic-only\n' \
  "$compiler_count" "$promoter_count"

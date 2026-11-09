#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/test/HPD-Agent.AudioCli/HPD-Agent.AudioCli.csproj"
CONFIGURATION="${HPD_AUDIO_SMOKE_CONFIGURATION:-Release}"
CHAT_MODEL="${HPD_AUDIO_SMOKE_CHAT_MODEL:-gpt-4.1-mini}"
STT_MODEL="${HPD_AUDIO_SMOKE_STT_MODEL:-gpt-4o-mini-transcribe}"
TTS_MODEL="${HPD_AUDIO_SMOKE_TTS_MODEL:-eleven_turbo_v2_5}"
MODE="${1:-all}"

if [[ -z "${OPENAI_API_KEY:-}" ]]; then
  echo "OPENAI_API_KEY is required for the live audio smoke test." >&2
  exit 2
fi

if [[ "$MODE" == "all" || "$MODE" == "batch" ]]; then
  if [[ -z "${ELEVENLABS_API_KEY:-}" ]]; then
    echo "ELEVENLABS_API_KEY is required for the batch STT/chat/TTS smoke test." >&2
    exit 2
  fi
fi

ARTIFACT_DIR="$(mktemp -d)"
trap 'rm -rf "$ARTIFACT_DIR"' EXIT

run_audio_cli() {
  dotnet run \
    --project "$PROJECT" \
    --configuration "$CONFIGURATION" \
    --no-build \
    -- "$@"
}

if [[ "$MODE" == "all" || "$MODE" == "batch" ]]; then
  echo "Running batch STT -> chat -> ElevenLabs TTS smoke test..."
  run_audio_cli \
    --audio "$ROOT/test/HPD-Agent.AudioCli/freesound_community-how-are-you-doing-today-103598.mp3" \
    --chat-model "$CHAT_MODEL" \
    --stt-provider openai \
    --stt-model "$STT_MODEL" \
    --tts \
    --tts-provider elevenlabs \
    --tts-model "$TTS_MODEL" \
    --tts-output-dir "$ARTIFACT_DIR" \
    --benchmark-output "$ARTIFACT_DIR/batch.jsonl"

  test -s "$ARTIFACT_DIR/batch.jsonl"
fi

if [[ "$MODE" == "all" || "$MODE" == "realtime" ]]; then
  echo "Running OpenAI realtime audio smoke test..."
  run_audio_cli \
    --audio "$ROOT/test/HPD-Agent.AudioCli/adih-crrentz7-my-name-is-jeff-148948.mp3" \
    --chat-model "$CHAT_MODEL" \
    --stt-provider openai \
    --stt-model "$STT_MODEL" \
    --no-tts \
    --realtime \
    --realtime-transcription \
    --benchmark-output "$ARTIFACT_DIR/realtime.jsonl"

  test -s "$ARTIFACT_DIR/realtime.jsonl"
fi

echo "Live audio smoke test completed."

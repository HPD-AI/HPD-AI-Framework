# HPD-Agent.Audio

HPD-owned audio contracts, deterministic runtime coordination, and `AgentBuilder`
integration for speech-to-text, text-to-speech, realtime audio, voice activity,
interruptions, and transport-backed live sessions.

## Install

```bash
dotnet add package HPD-Agent.Audio
dotnet add package HPD-Agent.Providers.Audio.OpenAI
dotnet add package HPD-Agent.Providers.Audio.ElevenLabs
```

Install only the provider packages used by the application. Local voice activity
is available through `HPD-Agent.Providers.Audio.Silero`.

## Package map

| Package or artifact | Purpose | 1.0 status |
| --- | --- | --- |
| `HPD-Agent.Audio` | Runtime contracts, orchestration, events, output sinks | Supported |
| `HPD-Agent.Providers.Audio.OpenAI` | Batch/realtime STT, TTS, realtime model audio | Supported |
| `HPD-Agent.Providers.Audio.ElevenLabs` | STT and TTS | Supported |
| `HPD-Agent.Providers.Audio.Silero` | Local PCM16 voice activity | Supported on qualified RIDs |
| `HPD-Agent.Audio.LiveKit` | Managed LiveKit room transport | Supported on `osx-arm64` |
| Browser Web Audio qualification | Virtual browser playout/capture checks | Private diagnostic |
| SIP/PBX telephony qualification | Asterisk/PCMU RTP checks | Private diagnostic |

Packable audio projects target `net8.0`, `net9.0`, and `net10.0` unless a
package documents a narrower runtime or platform scope.

## Concepts

- `AudioInputMode` selects batch STT, streaming STT, provider realtime audio, or
  reference-only behavior.
- `AudioOutputMode` selects text-only, completed-text TTS, or provider realtime
  audio.
- `AssistantOutputSynthesisMode` controls final-text synthesis and progressive
  synthesis.
- An `IAudioOutputSink` owns playback. HPD publishes audio even when no playback
  sink is attached.
- `AssistantAudioArtifactCapturePolicy` controls whether synthesized audio is
  stored as a content artifact, referenced by digest, or discarded.
- Audio work is instrumented with structured `AgentEvent` records, ledgers, and
  trace records.

## Text-to-speech

Configure a chat client and a TTS provider, then opt into audio output:

```csharp
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Providers.Audio.OpenAI;

var agent = await AgentBuilder.Create()
    .WithEventApplicationIdentity("Contoso.AudioApp")
    .WithChatClient(chatClient)
    .WithContentStore(contentStore)
    .WithOpenAITextToSpeech(
        model: "gpt-4o-mini-tts",
        voice: "alloy")
    .WithAudio(audio =>
    {
        audio.OutputMode = AudioOutputMode.TextToSpeech;
        audio.AssistantOutputMode = AssistantOutputSynthesisMode.FinalText;
        audio.ArtifactCapturePolicy =
            AssistantAudioArtifactCapturePolicy.ContentStoreArtifact;
    })
    .BuildAsync();
```

`OPENAI_API_KEY` is the default environment secret alias. Supply playback by
setting `AudioRuntimeAttachmentOptions.AssistantAudioOutputSink` and
`EnableAssistantOutputPlayback`, or use the managed LiveKit transport.

Subscribe to `AssistantAudioOutputArtifactCapturedEvent` for completed audio,
`AssistantAudioOutputChunkReadyEvent` for streamed chunks, and the playback
events when a sink reports playout truth.

## Batch speech-to-text

```csharp
.WithOpenAISpeechToText("gpt-4o-transcribe")
.WithAudio(audio =>
{
    audio.InputMode = AudioInputMode.BatchSpeechToText;
    audio.OutputMode = AudioOutputMode.TextOnly;
})
```

Send a `ChatMessage` containing `AudioContent`. The transcribed text is
projected into the user message before the chat model runs. Raw audio continues
to require a content store or a caller-owned input-content resolver.

## Realtime audio

```csharp
.WithOpenAIRealtime("gpt-realtime")
.WithAudio(audio =>
{
    audio.InputMode = AudioInputMode.ProviderRealtime;
    audio.OutputMode = AudioOutputMode.ProviderRealtimeAudio;
})
```

Realtime runs use `AgentModelTransportMode.Realtime`. Subscribe to
`UserAudioTranscriptDeltaEvent`, `UserAudioTranscriptCompletedEvent`, and
`UserAudioTranscriptFailedEvent` for input diagnostics.

## Failure diagnostics

Assistant audio failures carry `AudioErrorInfo`, including a stable code,
message, category, retryability, and optional provider metadata. Realtime user
transcript failures carry `ProviderErrorInfo` with normalized category,
retryability, status code, provider request ID, and retry delay when available.

Treat synthesis failure as recoverable text-only output when the chat response
itself succeeded. Treat transport or playback failure as an output-delivery
failure, not a failed model turn.

## Supported audio assumptions

- Managed retained STT currently requires decoded mono PCM16 input.
- OpenAI retained realtime transcription requires manual commit authority.
- Provider-native realtime audio has provider-specific format and lifecycle
  constraints.
- Playback is only reported as heard when the configured sink supplies
  authoritative playback evidence.
- Live transport support is separate from the core package. See the individual
  transport package README for platform and packaging constraints.

The `HPD-Agent.AudioCli` project in the repository is an executable diagnostic
harness for provider and transport qualification. It is not a replacement for
application-level playback or transport integration.

Repository maintainers can run the credentialed provider smoke test with:

```bash
OPENAI_API_KEY=... ELEVENLABS_API_KEY=... \
  bash eng/test-audio-live-smoke.sh all
```

The corresponding `HPD Agent Audio Live Smoke` workflow runs the same batch
STT/chat/TTS and realtime qualification paths.

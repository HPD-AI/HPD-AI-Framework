# HPD-Agent.Audio.LiveKit

Managed LiveKit transport for HPD Agent Audio sessions, including room
connection ownership, speech-to-text source wiring, audio output routing,
native-artifact verification, and lifecycle fencing.

## Install

```bash
dotnet add package HPD-Agent.Audio
dotnet add package HPD-Agent.Audio.LiveKit
dotnet add package HPD-Agent.Providers.Audio.OpenAI
```

The managed package depends on
`HPD-Agent.Audio.LiveKit.NativeAssets`. The current native qualification is
**`osx-arm64` only**. Other runtime identifiers fail closed with
`livekit-runtime-unsupported`.

## Direct builder integration

Configure STT and TTS first, then select the LiveKit transport:

```csharp
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.LiveKit;
using HPD.Agent.Providers.Audio.OpenAI;

var agent = await AgentBuilder.Create()
    .WithEventApplicationIdentity("Contoso.LiveAudio")
    .WithChatClient(chatClient)
    .WithContentStore(contentStore)
    .WithOpenAISpeechToText("gpt-4o-mini-transcribe")
    .WithOpenAITextToSpeech("gpt-4o-mini-tts", voice: "alloy")
    .WithLiveKitAudioTransport(
        endpoint: "wss://your-livekit-host",
        credentialResolver: ResolveParticipantCredentialAsync)
    .WithAudio(audio =>
    {
        audio.InputMode = AudioInputMode.StreamingSpeechToText;
        audio.OutputMode = AudioOutputMode.TextToSpeech;
        audio.AssistantOutputMode =
            AssistantOutputSynthesisMode.ProgressiveWithFinalFallback;
        audio.EnablePlayback = true;
    })
    .BuildAsync();
```

`credentialResolver` receives `ManagedAudioSessionStartRequest` and returns
either a managed LiveKit credential envelope or an already-minted participant
JWT as a `char[]`. HPD clears the returned buffer after use.

The session binding must provide `roomName` and `participantIdentity`. Use
`LiveKitAudioTransport.Bindings(...)` when starting a retained audio session
through the managed session authority.

## ASP.NET Core

Register the authority and backend through dependency injection:

```csharp
builder.Services.AddManagedLiveKitAudio(options);
```

Supply one `IAudioSessionControlAuthority` for the application lifetime and
attach it to either the builder or DI hosting path. Do not create a second owner
for the same room or session generation.

## Operational constraints

- Native runtime admission verifies the pinned artifact SHA-256 before use.
- Unsupported RIDs and hash mismatches are reported as safe, structured
  failures rather than falling back to an unverified library.
- The managed and native packages must remain version-aligned.
- Playback truth is emitted only after the LiveKit output path acknowledges the
  audio operation.
- Room credentials are resolved per session and are not stored in agent
  configuration.

See [HPD-Agent.Audio](../../HPD-Agent.Audio/README.md) for the provider-neutral
audio lifecycle and diagnostics contract.

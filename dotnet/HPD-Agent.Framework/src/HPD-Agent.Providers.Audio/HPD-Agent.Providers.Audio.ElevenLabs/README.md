# HPD-Agent.Providers.Audio.ElevenLabs

ElevenLabs speech-to-text and text-to-speech adapters for `HPD-Agent.Audio`.

## Install

```bash
dotnet add package HPD-Agent.Audio
dotnet add package HPD-Agent.Providers.Audio.ElevenLabs
```

## Authentication

The default secret alias is `ELEVENLABS_API_KEY`. The builder extensions also
accept a `ReadOnlySpan<char>` for runtime-only literal keys.

## Speech-to-text

```csharp
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Providers.Audio.ElevenLabs;

var agent = await AgentBuilder.Create()
    .WithChatClient(chatClient)
    .WithContentStore(contentStore)
    .WithElevenLabsSpeechToText(
        model: "scribe_v1",
        language: "en")
    .WithAudio(audio =>
    {
        audio.InputMode = AudioInputMode.BatchSpeechToText;
        audio.OutputMode = AudioOutputMode.TextOnly;
    })
    .BuildAsync();
```

The default batch model is `scribe_v1`. `scribe_v2` and
`scribe_v2_realtime` are also advertised. Use `ElevenLabsSttOptions` for
realtime model, audio format, commit strategy, timestamps, and language
detection settings.

## Text-to-speech

```csharp
.WithElevenLabsTextToSpeech(
    model: "eleven_turbo_v2_5",
    voice: voiceId,
    outputFormat: "mp3_44100_128")
.WithAudio(audio =>
{
    audio.OutputMode = AudioOutputMode.TextToSpeech;
    audio.AssistantOutputMode = AssistantOutputSynthesisMode.FinalText;
})
```

The default model is `eleven_turbo_v2_5`. Other supported model IDs include
`eleven_multilingual_v2` and `eleven_flash_v2_5`. The voice ID comes from the
ElevenLabs account and must be supplied explicitly unless configured in an
agent profile.

The adapter supports completed-text synthesis. Progressive output is managed by
HPD and may segment completed model text; provider capability metadata remains
the source of truth for transport-specific behavior.

## Related documentation

- [HPD-Agent.Audio](../../HPD-Agent.Audio/HPD-Agent.Audio/README.md)
- [HPD-Agent error handling](../../HPD-Agent/ErrorHandling/README.md)

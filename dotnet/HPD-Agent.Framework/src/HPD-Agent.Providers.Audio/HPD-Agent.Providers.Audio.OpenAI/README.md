# HPD-Agent.Providers.Audio.OpenAI

OpenAI speech-to-text, text-to-speech, and realtime adapters for
`HPD-Agent.Audio`.

## Install

```bash
dotnet add package HPD-Agent.Audio
dotnet add package HPD-Agent.Providers.Audio.OpenAI
dotnet add package HPD-Agent.Providers.OpenAI
```

The package registers the `openai/platform` provider contribution. It composes
with `HPD-Agent.Providers.OpenAI`, so the same application can use OpenAI chat,
embeddings, STT, TTS, and realtime clients without duplicate provider
registration.

## Authentication

The default secret aliases are:

- `OPENAI_API_KEY`
- `OPENAI_ENDPOINT`

For runtime-only literal keys, use the `ReadOnlySpan<char>` overloads of the
builder extensions. HPD registers the key without persisting it in agent
configuration.

## Speech-to-text

```csharp
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Providers.Audio.OpenAI;

var agent = await AgentBuilder.Create()
    .WithChatClient(chatClient)
    .WithContentStore(contentStore)
    .WithOpenAISpeechToText("gpt-4o-transcribe")
    .WithAudio(audio =>
    {
        audio.InputMode = AudioInputMode.BatchSpeechToText;
        audio.OutputMode = AudioOutputMode.TextOnly;
    })
    .BuildAsync();
```

Supported audio models include `whisper-1`, `gpt-4o-transcribe`,
`gpt-4o-mini-transcribe`, and `gpt-transcribe`. Use `OpenAISttOptions` for
response format, prompt, temperature, timestamps, and logprobs.

## Text-to-speech

```csharp
.WithOpenAITextToSpeech(
    model: "gpt-4o-mini-tts",
    voice: "alloy",
    outputFormat: "mp3")
.WithAudio(audio =>
{
    audio.OutputMode = AudioOutputMode.TextToSpeech;
    audio.AssistantOutputMode = AssistantOutputSynthesisMode.FinalText;
})
```

Supported TTS models are `tts-1`, `tts-1-hd`, and `gpt-4o-mini-tts`. The OpenAI
adapter supports completed-text synthesis; it does not advertise provider-native
incremental text input.

## Realtime

```csharp
.WithOpenAIRealtime("gpt-realtime")
.WithAudio(audio =>
{
    audio.InputMode = AudioInputMode.ProviderRealtime;
    audio.OutputMode = AudioOutputMode.ProviderRealtimeAudio;
})
```

The default realtime model is `gpt-realtime`. Realtime failures and user input
transcripts are observable through `AgentEvent` subscriptions. Input transcript
failures include structured `ProviderErrorInfo` when the provider supplies an
error code.

## Related documentation

- [HPD-Agent.Audio](../../HPD-Agent.Audio/HPD-Agent.Audio/README.md)
- [HPD-Agent error handling](../../HPD-Agent/ErrorHandling/README.md)

# HPD-Agent.AudioProviders.ElevenLabs

ElevenLabs audio provider for HPD-Agent Framework. Provides high-quality text-to-speech and speech-to-text capabilities using the ElevenLabs API.

## Features

-   **Text-to-Speech (TTS)** - High-quality voice synthesis with 32 languages
-   **Speech-to-Text (STT)** - Accurate transcription via ElevenLabs Scribe
-   **Streaming Support** - HTTP-based audio streaming
-   **WebRTC Ready** - Real-time communication via ConvAI agents
-   **Voice Cloning** - Custom voice creation
-   **Multilingual** - Support for 32 languages
-   **Character Timestamps** - Word-level alignment data
-   **Dubbing & Sound Generation** - Via ElevenLabs extended features

## Installation

```bash
dotnet add package HPD-Agent.AudioProviders.ElevenLabs
```

## Quick Start

### Basic Usage

```csharp
using HPD.Agent.Audio.ElevenLabs;

// Create config with your API key
var config = new ElevenLabsAudioConfig
{
    ApiKey = "your-api-key-here"  // Get at: https://elevenlabs.io/app/settings/api-keys
};

// Create provider
using var provider = new ElevenLabsAudioProvider(config);

// Get TTS client
var ttsClient = provider.GetTextToSpeechClient();

// Generate speech
var response = await ttsClient.GetSpeechAsync("Hello, world!");
await File.WriteAllBytesAsync("output.mp3", response.Audio.Data.ToArray());
```

### Use with Audio Pipeline Middleware

```csharp
using HPD.Agent.Audio;
using HPD.Agent.Audio.ElevenLabs;

// Create ElevenLabs provider
var elevenLabsConfig = new ElevenLabsAudioConfig
{
    ApiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY"),
    DefaultVoiceId = "21m00Tcm4TlvDq8ikWAM",  // Rachel voice
    ModelId = "eleven_turbo_v2_5",             // Low latency model
    Stability = 0.5f,
    SimilarityBoost = 0.75f
};

var provider = new ElevenLabsAudioProvider(elevenLabsConfig);

// Configure audio middleware
var audioMiddleware = new AudioPipelineMiddleware
{
    TextToSpeechClient = provider.GetTextToSpeechClient(),
    SpeechToTextClient = provider.GetSpeechToTextClient(),
    DefaultVoice = "Rachel",  // Will use DefaultVoiceId from config
    EnableQuickAnswer = true,
    EnableFillerAudio = true
};

// Add to agent
agent.Use(audioMiddleware);
```

### Environment-Based Configuration

```csharp
// Set environment variable
// export ELEVENLABS_API_KEY=your-api-key

// Create provider from environment
using var provider = ElevenLabsAudioProviderModule.CreateProviderFromEnvironment();
```

## Configuration

### Core Settings

```csharp
var config = new ElevenLabsAudioConfig
{
    // Required
    ApiKey = "your-api-key",

    // TTS Settings
    DefaultVoiceId = "21m00Tcm4TlvDq8ikWAM",  // Rachel (premade voice)
    ModelId = "eleven_turbo_v2_5",             // Fast, 32 languages
    OutputFormat = "mp3_44100_128",            // Audio quality

    // Voice Settings
    Stability = 0.5f,           // 0-1 (higher = more consistent)
    SimilarityBoost = 0.75f,    // 0-1 (higher = closer to original voice)
    Style = 0.0f,               // 0-1 (higher = more expressive)
    UseSpeakerBoost = true,     // Enhance clarity

    // STT Settings
    SttModelId = "scribe_v1",
    SttResponseFormat = "json",

    // Timeouts
    HttpTimeout = TimeSpan.FromSeconds(30),
    WebSocketConnectTimeout = TimeSpan.FromSeconds(10)
};
```

### Available Models

| Model | Description | Latency | Languages |
|-------|-------------|---------|-----------|
| `eleven_flash_v2` | Default, balanced | <75ms | Multilingual |
| `eleven_flash_v2_5` | English only | <50ms | English |
| `eleven_turbo_v2` | Low latency | <100ms | English |
| `eleven_turbo_v2_5` | Fast, multilingual | <100ms | 32 languages |
| `eleven_multilingual_v2` | High quality | ~200ms | 29 languages |
| `eleven_monolingual_v1` | Legacy | ~200ms | English |

### Output Formats

```csharp
// MP3 (default)
"mp3_44100_128"  // 44.1kHz, 128kbps
"mp3_44100_64"   // 64kbps
"mp3_44100_96"   // 96kbps
"mp3_44100_192"  // 192kbps (Creator+ tier)

// PCM (raw audio)
"pcm_16000"   // 16kHz
"pcm_22050"   // 22.05kHz
"pcm_24000"   // 24kHz
"pcm_44100"   // 44.1kHz (Indie+ tier)

// μ-law
"ulaw_8000"   // 8kHz telephony
```

### Premade Voices

```csharp
// Popular ElevenLabs voices (voice IDs)
var voices = new Dictionary<string, string>
{
    ["Rachel"] = "21m00Tcm4TlvDq8ikWAM",
    ["Domi"] = "AZnzlk1XvdvUeBnXmlld",
    ["Bella"] = "EXAVITQu4vr4xnSDxMaL",
    ["Antoni"] = "ErXwobaYiN019PkySvjV",
    ["Elli"] = "MF3mGyEYCl7XYWbV9V6O",
    ["Josh"] = "TxGEqnHWrfWFTfGW9XjX",
    ["Arnold"] = "VR6AewLTigWG4xSOukaG",
    ["Adam"] = "pNInz6obpgDQGcFmaJgB",
    ["Sam"] = "yoZ06aMxZJJ28mfd3POQ"
};

config.DefaultVoiceId = voices["Rachel"];
```

## Advanced Features

### Streaming TTS

```csharp
var ttsClient = provider.GetTextToSpeechClient();

await foreach (var chunk in ttsClient.GetStreamingSpeechAsync(textChunks))
{
    // Process audio chunks as they arrive
    await audioStream.WriteAsync(chunk.Audio.Data);
}
```

### Speech-to-Text

```csharp
var sttClient = provider.GetSpeechToTextClient();

using var audioFile = File.OpenRead("recording.wav");
var transcription = await sttClient.GetTextAsync(audioFile);

Console.WriteLine(transcription.Text);
```

### Voice Settings Per Request

```csharp
var options = new TextToSpeechOptions
{
    Voice = "21m00Tcm4TlvDq8ikWAM",  // Rachel
    AdditionalProperties = new Dictionary<string, object?>
    {
        ["Stability"] = 0.8f,
        ["SimilarityBoost"] = 0.9f,
        ["Style"] = 0.5f,
        ["UseSpeakerBoost"] = true
    }
};

var response = await ttsClient.GetSpeechAsync("Hello!", options);
```

## Integration with HPD Audio Pipeline

### Complete Example

```csharp
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.ElevenLabs;

// Create agent
var agent = new AgentBuilder()
    .WithModel("gpt-4o")
    .Build();

// Configure ElevenLabs
var elevenLabsConfig = new ElevenLabsAudioConfig
{
    ApiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY"),
    DefaultVoiceId = "21m00Tcm4TlvDq8ikWAM",  // Rachel
    ModelId = "eleven_turbo_v2_5",
    Stability = 0.5f,
    SimilarityBoost = 0.75f,
    OutputFormat = "mp3_44100_128"
};

var provider = new ElevenLabsAudioProvider(elevenLabsConfig);

// Configure audio pipeline
var audioMiddleware = new AudioPipelineMiddleware
{
    TextToSpeechClient = provider.GetTextToSpeechClient(),
    SpeechToTextClient = provider.GetSpeechToTextClient(),

    // Quick Answer (TTS on first sentence)
    EnableQuickAnswer = true,

    // Filler Audio (natural "um" sounds)
    EnableFillerAudio = true,
    FillerPhrases = new[] { "Hmm...", "Let me think...", "One moment..." },

    // Text Filtering (remove code blocks for TTS)
    EnableTextFiltering = true,
    FilterCodeBlocks = true
};

agent.Use(audioMiddleware);

// Run agent with audio
var response = await agent.RunAsync("Explain quantum computing");
```

## Provider Capabilities

```csharp
var features = ElevenLabsAudioProviderModule.GetFeatures();

Console.WriteLine($"Provider: {features.ProviderName}");
Console.WriteLine($"TTS: {features.SupportsTextToSpeech}");
Console.WriteLine($"STT: {features.SupportsSpeechToText}");
Console.WriteLine($"Streaming: {features.SupportsStreaming}");
Console.WriteLine($"WebRTC: {features.SupportsWebRTC}");
Console.WriteLine($"Voice Cloning: {features.SupportsVoiceCloning}");
Console.WriteLine($"Languages: {string.Join(", ", features.SupportedLanguages)}");
```

## Comparison to Other Providers

| Feature | ElevenLabs | OpenAI TTS | Azure TTS |
|---------|------------|------------|-----------|
| **Voice Quality** | ⭐⭐⭐⭐⭐ (Best) | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Latency (Turbo)** | <100ms | ~500ms | ~300ms |
| **Languages** | 32 | 57 | 100+ |
| **Voice Cloning** |   Yes | No | Neural only |
| **Pricing (1M chars)** | ~$300 | ~$15 | ~$16 |
| **STT Quality** | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Real-time** |   WebRTC |   Realtime API |   WebSocket |

**Best for:**
- **High-quality voice experiences** (podcasts, audiobooks, premium apps)
- **Emotional/conversational AI** (customer service, therapy bots)
- **Voice cloning** (personalized assistants)
- **Multi-language apps** (32 languages)

**Not ideal for:**
- **High-volume/low-cost** (use OpenAI/Deepgram instead)
- **Simple TTS** (OpenAI is simpler for basic use)
- **Ultra-low latency** (use Cartesia - 40ms TTFB)

## Troubleshooting

### API Key Issues

```csharp
// Validate config before using
config.Validate();  // Throws if API key missing
```

### Rate Limits

ElevenLabs has tiered rate limits:
- **Free**: 10,000 chars/month
- **Starter**: 30,000 chars/month
- **Creator**: 100,000 chars/month
- **Pro**: 500,000 chars/month

### Timeout Configuration

```csharp
config.HttpTimeout = TimeSpan.FromSeconds(60);  // For long audio
```

## Resources

- **ElevenLabs Dashboard**: https://elevenlabs.io/app
- **API Documentation**: https://elevenlabs.io/docs
- **Voice Library**: https://elevenlabs.io/app/voice-library
- **API Keys**: https://elevenlabs.io/app/settings/api-keys
- **Pricing**: https://elevenlabs.io/pricing

## License

Copyright (c) 2025 Einstein Essibu. All rights reserved.

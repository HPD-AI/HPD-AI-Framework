# HPD-Agent.Providers.GoogleAI

Google AI (Gemini) provider for HPD-Agent.

## Overview

This NuGet package provides an `IChatClient` implementation for Google AI (Gemini) models, compatible with Microsoft.Extensions.AI. It wraps the official [Google GenerativeAI SDK](https://github.com/googleapis/google-cloud-dotnet/tree/main/apis/Google.Cloud.AIPlatform.V1).

## Features

- Streaming and non-streaming chat completions
- Function calling (tool use)
- Vision/image inputs
- Structured JSON output
- Deterministic generation with seed
- Sampling parameter control
- System prompts

## Getting Started

```csharp
using HPD.Agent;
using HPD.Agent.Providers.GoogleAI;

var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.MaxOutputTokens = 8192;
            opts.Temperature = 0.7;
        })
    .Build();

var response = await agent.ChatAsync("What is the capital of France?");
```

## Configuration

### Environment Variables

```bash
export GOOGLE_AI_API_KEY="your-api-key"
# or
export GEMINI_API_KEY="your-api-key"
```

### appsettings.json

```json
{
  "Agent": {
    "Provider": {
      "ProviderKey": "google-ai",
      "ModelName": "gemini-2.0-flash",
      "ApiKey": "your-api-key"
    }
  }
}
```

## Configuration Options

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.MaxOutputTokens = 8192;
            opts.Temperature = 0.7;
            opts.TopP = 0.95;
            opts.TopK = 40;
            opts.Seed = 12345;
        })
    .Build();
```

## Supported Models

- `gemini-2.0-flash` - Latest fast model
- `gemini-1.5-pro` - Advanced reasoning model
- `gemini-1.5-flash` - Efficient model

## API Key

Get your API key from [Google AI Studio](https://aistudio.google.com/app/apikey).

## Documentation

- [Google AI Documentation](https://ai.google.dev/)
- [Gemini API Reference](https://ai.google.dev/api/generate-content)
- [HPD-Agent Documentation](../../README.md)

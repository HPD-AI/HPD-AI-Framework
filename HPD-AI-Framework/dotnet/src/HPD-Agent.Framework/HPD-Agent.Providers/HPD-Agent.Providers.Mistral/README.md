# HPD-Agent.Providers.Mistral

Mistral AI provider for HPD-Agent.

## Overview

This NuGet package provides an `IChatClient` implementation for Mistral AI models, compatible with Microsoft.Extensions.AI. It wraps the Mistral.SDK to access Mistral's chat completions API.

## Features

- Streaming and non-streaming chat completions
- Function calling (tool use)
- JSON mode support
- Sampling parameter control
- Deterministic generation with seed
- Safety prompt guardrails

## Getting Started

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Mistral;

var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.MaxTokens = 4096;
            opts.Temperature = 0.7m;
        })
    .Build();

var response = await agent.ChatAsync("Hello!");
```

## Configuration

### Environment Variables

```bash
export MISTRAL_API_KEY="your-api-key"
```

### appsettings.json

```json
{
  "Mistral": {
    "ApiKey": "your-api-key",
    "ModelName": "mistral-large-latest"
  }
}
```

## Configuration Options

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.MaxTokens = 4096;
            opts.Temperature = 0.7m;
            opts.TopP = 0.95m;
            opts.RandomSeed = 12345;
        })
    .Build();
```

## Supported Models

- `mistral-large-latest` - Most powerful model
- `mistral-medium-latest` - Balanced performance
- `mistral-small-latest` - Fast and cost-effective
- `open-mistral-7b` - Open-source model
- `open-mixtral-8x7b` - Mixture of Experts model

## API Key

Get your API key from [console.mistral.ai](https://console.mistral.ai/api-keys/).

## Documentation

- [Mistral AI Documentation](https://docs.mistral.ai/)
- [Available Models](https://docs.mistral.ai/getting-started/models/)
- [HPD-Agent Documentation](../../README.md)

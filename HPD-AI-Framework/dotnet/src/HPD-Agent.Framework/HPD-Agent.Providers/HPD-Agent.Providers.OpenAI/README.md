# HPD-Agent.Providers.OpenAI

OpenAI and Azure OpenAI provider for HPD-Agent.

## Overview

This NuGet package provides an `IChatClient` implementation for OpenAI and Azure OpenAI APIs, compatible with Microsoft.Extensions.AI. It wraps the official [OpenAI .NET SDK](https://github.com/openai/openai-dotnet).

## Features

- OpenAI and Azure OpenAI support
- Streaming and non-streaming chat completions
- Function calling (tool use)
- Vision (image understanding)
- Audio input/output (gpt-4o-audio)
- Reasoning models (o1 series)
- Structured JSON output with schema validation
- Deterministic generation with seed

## Getting Started

### OpenAI

```csharp
using HPD.Agent;
using HPD.Agent.Providers.OpenAI;

var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-your-key",
        configure: opts =>
        {
            opts.MaxOutputTokenCount = 4096;
            opts.Temperature = 0.7f;
        })
    .Build();

var response = await agent.ChatAsync("Hello!");
```

### Azure OpenAI

```csharp
var agent = await new AgentBuilder()
    .WithAzureOpenAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.MaxOutputTokenCount = 4096;
            opts.Temperature = 0.7f;
        })
    .Build();
```

## Configuration

### Environment Variables

**OpenAI:**
```bash
export OPENAI_API_KEY="sk-..."
```

**Azure OpenAI:**
```bash
export AZURE_OPENAI_ENDPOINT="https://my-resource.openai.azure.com"
export AZURE_OPENAI_API_KEY="your-api-key"
```

### appsettings.json

```json
{
  "OpenAI": {
    "ApiKey": "sk-..."
  },
  "AzureOpenAI": {
    "Endpoint": "https://my-resource.openai.azure.com",
    "ApiKey": "your-api-key"
  }
}
```

## Configuration Options

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts =>
        {
            opts.MaxOutputTokenCount = 4096;
            opts.Temperature = 0.7f;
            opts.TopP = 0.95f;
            opts.Seed = 12345;
            opts.ResponseFormat = "json_schema";
        })
    .Build();
```

## Supported Models

### OpenAI
- `gpt-4o` - Latest GPT-4 model
- `gpt-4-turbo` - Advanced reasoning
- `gpt-4`, `gpt-3.5-turbo` - Earlier models
- `o1-preview`, `o1-mini` - Reasoning models
- `gpt-4o-audio-preview` - Audio support

### Azure OpenAI
All deployed models on Azure OpenAI Service

## Documentation

- [OpenAI Documentation](https://platform.openai.com/docs)
- [Azure OpenAI Documentation](https://learn.microsoft.com/azure/ai-services/openai/)
- [OpenAI .NET SDK](https://github.com/openai/openai-dotnet)
- [HPD-Agent Documentation](../../README.md)

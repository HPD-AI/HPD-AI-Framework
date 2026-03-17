# HPD-Agent.Providers.AzureAIInference

> **DEPRECATION NOTICE**: This provider is deprecated. Use `HPD-Agent.Providers.AzureAI` instead.

Azure AI Inference provider for HPD-Agent.

## Overview

This NuGet package provides an `IChatClient` implementation for Azure AI Inference endpoints, compatible with Microsoft.Extensions.AI. It wraps the official [Azure AI Inference SDK](https://learn.microsoft.com/en-us/azure/ai-studio/how-to/deploy-models-inference).

## Getting Started

```csharp
using HPD.Agent;
using HPD.Agent.Providers.AzureAIInference;

var agent = await new AgentBuilder()
    .WithAzureAIInference(
        endpoint: "https://your-resource.inference.ai.azure.com",
        model: "llama-3-8b",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.MaxTokens = 2048;
            opts.Temperature = 0.7f;
        })
    .Build();

var response = await agent.ChatAsync("Your prompt here");
```

## Configuration

### Environment Variables

```bash
export AZURE_AI_INFERENCE_ENDPOINT="https://your-resource.inference.ai.azure.com"
export AZURE_AI_INFERENCE_API_KEY="your-api-key"
```

### appsettings.json

```json
{
  "Agent": {
    "Provider": {
      "ProviderKey": "azure-ai-inference",
      "ModelName": "llama-3-8b",
      "Endpoint": "https://your-resource.inference.ai.azure.com",
      "ApiKey": "your-api-key"
    }
  }
}
```

## Features

- Streaming and non-streaming completions
- Function calling (tools)
- JSON mode and structured outputs
- Deterministic generation with seed
- Sampling parameter control
- Comprehensive error handling with retry logic

## Configuration Options

```csharp
var agent = await new AgentBuilder()
    .WithAzureAIInference(
        endpoint: "https://your-resource.inference.ai.azure.com",
        model: "llama-3-8b",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.MaxTokens = 2048;
            opts.Temperature = 0.7f;
            opts.TopP = 0.9f;
            opts.Seed = 12345;
        })
    .Build();
```

## Limitations

- Not compatible with Native AOT deployments (SDK limitation)

## Migration

For Azure AI Foundry endpoints, migrate to `HPD-Agent.Providers.AzureAI`:

```csharp
using HPD.Agent.Providers.AzureAI;

var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://your-account.services.ai.azure.com/api/projects/your-project",
        model: "gpt-4",
        configure: opts => opts.UseDefaultAzureCredential = true)
    .Build();
```

## Documentation

- [Azure AI Inference Documentation](https://learn.microsoft.com/en-us/azure/ai-studio/how-to/deploy-models-inference)
- [HPD-Agent Documentation](../../README.md)

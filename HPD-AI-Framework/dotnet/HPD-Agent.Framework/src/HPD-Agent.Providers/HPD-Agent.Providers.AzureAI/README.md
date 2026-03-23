# HPD-Agent.Providers.AzureAI

Azure AI provider for HPD-Agent.

## Overview

This NuGet package provides an `IChatClient` implementation for Azure AI services, compatible with Microsoft.Extensions.AI. It supports both Azure AI Foundry (Projects) and traditional Azure OpenAI endpoints.

## Features

- Azure AI Foundry / Projects support
- Azure OpenAI support
- OAuth/Entra ID authentication (DefaultAzureCredential)
- API key authentication
- Tool/function calling
- Structured JSON output
- Extended thinking and other advanced features

## Getting Started

```csharp
using HPD.Agent;
using HPD.Agent.Providers.AzureAI;

// With OAuth (Recommended)
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-account.services.ai.azure.com/api/projects/my-project",
        model: "gpt-4",
        configure: opts =>
        {
            opts.UseDefaultAzureCredential = true;
            opts.MaxTokens = 4096;
        })
    .Build();

// With API Key
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-account.services.ai.azure.com/api/projects/my-project",
        model: "gpt-4",
        apiKey: "your-api-key")
    .Build();

var response = await agent.SendAsync("Hello!");
```

## Configuration

### Environment Variables

```bash
AZURE_AI_ENDPOINT=https://my-account.services.ai.azure.com/api/projects/my-project
AZURE_AI_API_KEY=your-api-key  # Optional if using OAuth
```

### appsettings.json

```json
{
  "AzureAI": {
    "Endpoint": "https://my-account.services.ai.azure.com/api/projects/my-project",
    "ApiKey": "your-api-key",
    "ModelName": "gpt-4"
  }
}
```

## Authentication

- **DefaultAzureCredential (OAuth)**: Recommended for production. Supports managed identity, Azure CLI, Visual Studio, and interactive browser flows.
- **API Key**: Simpler but less secure. Suitable for development.

## Supported Endpoints

- Azure AI Foundry: `https://<account>.services.ai.azure.com/api/projects/<project-name>`
- Azure OpenAI: `https://<resource>.openai.azure.com`

## Supported Models

All Azure OpenAI models are supported:
- GPT-4, GPT-4 Turbo, GPT-4o
- GPT-3.5 Turbo
- Custom fine-tuned models

## Documentation

- [Azure AI Documentation](https://learn.microsoft.com/en-us/azure/ai-studio/)
- [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [HPD-Agent Documentation](../../README.md)

# HPD-Agent.Providers.Anthropic

Anthropic (Claude) provider for HPD-Agent.

## Overview

This NuGet package provides an `IChatClient` implementation for the [Anthropic API](https://www.anthropic.com/), compatible with Microsoft.Extensions.AI. It wraps the official [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp).

## Features

- Streaming and non-streaming chat completions
- Function calling (tool use)
- Vision/image inputs
- Extended thinking mode
- Prompt caching
- Multi-modal content support
- System prompts

## Getting Started

```csharp
var config = new ProviderConfig
{
    ProviderKey = "anthropic",
    ApiKey = "your-api-key",
    ModelName = "claude-3-5-sonnet-20241022"
};

var agent = new AgentBuilder()
    .WithProviderConfig(config)
    .WithToolkit<MyToolkit>()
    .Build();

var response = await agent.ChatAsync("Your prompt here");
```

## Configuration

```csharp
var anthropicConfig = new AnthropicProviderConfig
{
    MaxTokens = 4096,
    EnablePromptCaching = true,
    Temperature = 1.0f,
    TopP = 0.95f
};

config.SetTypedProviderConfig(anthropicConfig);
```

## Limitations

- Not compatible with Native AOT deployments (SDK limitation)

## Documentation

- [HPD-Agent Documentation](../../README.md)
- [Anthropic API Documentation](https://docs.anthropic.com/)

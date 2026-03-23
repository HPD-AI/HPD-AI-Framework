# HPD-Agent.Providers.HuggingFace

HuggingFace provider for HPD-Agent.

## Overview

This NuGet package provides an `IChatClient` implementation for HuggingFace's Serverless Inference API, compatible with Microsoft.Extensions.AI. It provides free access to thousands of models hosted on HuggingFace.

## Features

- Streaming and non-streaming text generation
- Access to thousands of open-source models
- Automatic model loading and caching
- Sampling parameter control
- Support for code generation models
- Text generation models (LLaMA, Mistral, etc.)

## Getting Started

```csharp
using HPD.Agent;
using HPD.Agent.Providers.HuggingFace;

var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-8B-Instruct",
        apiKey: "hf_your-token",
        configure: opts =>
        {
            opts.MaxNewTokens = 500;
            opts.Temperature = 0.7;
        })
    .Build();

var response = await agent.ChatAsync("Write a Python function to calculate factorial");
```

## Configuration

### Environment Variables

```bash
export HF_TOKEN="hf_your-token"
# or
export HUGGINGFACE_API_KEY="hf_your-token"
```

### appsettings.json

```json
{
  "Agent": {
    "Provider": {
      "ProviderKey": "huggingface",
      "ModelName": "meta-llama/Meta-Llama-3-8B-Instruct",
      "ApiKey": "hf_your-token"
    }
  }
}
```

## Configuration Options

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-8B-Instruct",
        apiKey: "hf_your-token",
        configure: opts =>
        {
            opts.MaxNewTokens = 500;
            opts.Temperature = 0.7;
            opts.TopP = 0.95;
            opts.RepetitionPenalty = 1.1;
            opts.WaitForModel = true;
        })
    .Build();
```

## Popular Models

- `meta-llama/Meta-Llama-3-8B-Instruct` - LLaMA 3 chat model
- `mistralai/Mistral-7B-Instruct-v0.2` - Mistral chat model
- `HuggingFaceH4/zephyr-7b-beta` - Zephyr model
- `bigcode/starcoder2-15b` - Code generation

## API Key

Get your HuggingFace token from [huggingface.co/settings/tokens](https://huggingface.co/settings/tokens).

## Model Browser

Browse available models at [huggingface.co/models](https://huggingface.co/models).

## Documentation

- [HuggingFace Inference API Documentation](https://huggingface.co/docs/api-inference)
- [HuggingFace Models](https://huggingface.co/models)
- [HPD-Agent Documentation](../../README.md)

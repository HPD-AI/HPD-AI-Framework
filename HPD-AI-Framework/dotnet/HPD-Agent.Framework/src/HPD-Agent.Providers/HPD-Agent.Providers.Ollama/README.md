# HPD-Agent.Providers.Ollama

Ollama provider for HPD-Agent.

## Overview

This NuGet package provides an `IChatClient` implementation for local and remote Ollama instances, compatible with Microsoft.Extensions.AI. It wraps OllamaSharp to access Ollama's chat completions API.

## Features

- Local and remote Ollama support
- All Ollama models supported
- Function calling for compatible models
- Vision model support
- Streaming responses
- Reasoning model support (DeepSeek-R1, etc.)
- Fine-grained configuration options
- Native AOT compatible

## Getting Started

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Ollama;

// Local Ollama
var agent = await new AgentBuilder()
    .WithOllama(model: "llama3:8b")
    .Build();

// Remote Ollama
var agent = await new AgentBuilder()
    .WithOllama(
        model: "mistral",
        endpoint: "http://my-server:11434")
    .Build();

var response = await agent.ChatAsync("Hello!");
```

## Configuration

### Environment Variables

```bash
export OLLAMA_ENDPOINT="http://localhost:11434"
# or
export OLLAMA_HOST="http://localhost:11434"
```

### appsettings.json

```json
{
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "ModelName": "llama3:8b"
  }
}
```

## Configuration Options

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "llama3:8b",
        configure: opts =>
        {
            opts.Temperature = 0.7f;
            opts.NumPredict = 2048;
            opts.NumCtx = 4096;
            opts.TopP = 0.9f;
            opts.TopK = 40;
            opts.Seed = 12345;
        })
    .Build();
```

## Key Parameters

- `NumPredict` - Max tokens to generate (default: 128)
- `NumCtx` - Context window size (default: 2048)
- `Temperature` - Randomness 0.0-2.0 (default: 0.8)
- `TopP` - Nucleus sampling 0.0-1.0 (default: 0.9)
- `TopK` - Top K tokens (default: 40)
- `Seed` - Reproducible generation
- `NumGpu` - GPU layers to offload
- `Format` - Output format ("json", schema object)
- `Think` - Enable reasoning (for compatible models)

## Popular Models

- `llama3:8b`, `llama3:70b` - General purpose
- `mistral` - Fast and efficient
- `deepseek-r1:8b` - Reasoning model
- `llama3.2-vision` - Vision/image understanding
- `qwen2.5-coder` - Code generation

## Pull Models

```bash
ollama pull llama3:8b
ollama pull mistral
ollama pull deepseek-r1:8b
```

## Documentation

- [Ollama Official Site](https://ollama.com/)
- [Available Models](https://ollama.com/library)
- [Ollama API Documentation](https://github.com/ollama/ollama/blob/main/docs/api.md)
- [HPD-Agent Documentation](../../README.md)

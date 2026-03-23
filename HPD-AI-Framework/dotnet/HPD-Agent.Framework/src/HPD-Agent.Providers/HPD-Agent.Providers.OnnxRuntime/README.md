# HPD-Agent.Providers.OnnxRuntime

ONNX Runtime provider for HPD-Agent for local model inference.

## Overview

This package provides an integration with [ONNX Runtime](https://onnxruntime.ai/) for running models locally on your machine, compatible with Microsoft.Extensions.AI.

## Limitations

**Function calling (tool use) is not supported** with this provider. ONNX Runtime models do not have built-in support for structured tool calling. Use cloud-based providers (OpenAI, Anthropic, Azure AI, etc.) for function calling support.

## Configuration

To use the OnnxRuntime provider, you must specify the path to your local model.

### C# Configuration

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "path/to/your/onnx/model/directory")
    .Build();

var response = await agent.ChatAsync("Hello!");
```

### JSON Configuration (`appsettings.json`)

```json
{
  "Agent": {
    "Provider": {
      "ProviderKey": "onnx-runtime",
      "AdditionalProperties": {
        "ModelPath": "path/to/your/onnx/model/directory"
      }
    }
  }
}
```

### Environment Variables

```bash
export ONNX_MODEL_PATH="path/to/your/onnx/model/directory"
```

## Configuration Options

| Key | Type | Description |
|-----|------|-------------|
| `ModelPath` | string | **Required.** Path to the ONNX model directory. Can also use `ONNX_MODEL_PATH` environment variable. |
| `StopSequences` | `IList<string>` | Optional. Sequences that stop generation. |
| `EnableCaching` | bool | Optional. Enable conversation caching. Defaults to `false`. |
| `PromptFormatter` | function | Optional. Custom prompt formatting function. |

## Features

- Local model inference
- No API key required
- Privacy-focused (models run locally)
- Cross-platform support

## Documentation

- [ONNX Runtime Documentation](https://onnxruntime.ai/)
- [HPD-Agent Documentation](../../README.md)

# Ollama Provider

**Provider Key:** `ollama`

## Overview

The Ollama provider enables HPD-Agent to use local and remote Ollama instances for AI inference. Ollama allows you to run large language models locally on your machine or connect to remote Ollama servers, providing privacy, control, and zero API costs.

**Key Features:**
-  Local and remote model execution
-  All Ollama models supported (Llama, Mistral, Qwen, DeepSeek, Gemma, etc.)
-  Streaming support for real-time responses
-  Function/tool calling (model-dependent)
-  Vision support for multimodal models
-  Comprehensive parameter control (40+ options)
-  Reasoning model support (DeepSeek-R1, etc.)
-  JSON output mode
-  Performance tuning (GPU offloading, threading, memory management)
-  No authentication required for local instances

**For detailed API documentation, see:**
- [**OllamaProviderConfig API Reference**](#ollamaproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Ollama;

// Uses local Ollama at http://localhost:11434
var agent = await new AgentBuilder()
    .WithOllama(model: "llama3:8b")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.Ollama
```

**Dependencies:**
- `OllamaSharp` - .NET client library for Ollama API
- `Microsoft.Extensions.AI` - AI abstractions

**Prerequisites:**
- Ollama installed and running ([Download Ollama](https://ollama.com/download))
- At least one model pulled: `ollama pull llama3:8b`

## Configuration

### Configuration Patterns

The Ollama provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

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
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`ollama-config.json`:**
```json
{
    "name": "OllamaAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "ollama",
        "modelName": "llama3:8b",
        "endpoint": "http://localhost:11434",
        "providerOptionsJson": "{\"temperature\":0.7,\"numPredict\":2048,\"numCtx\":4096}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("ollama-config.json");
```

The `providerOptionsJson` value is a JSON string containing Ollama-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "OllamaAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "ollama",
        ModelName = "llama3:8b",
        Endpoint = "http://localhost:11434"
    }
};

config.Provider.SetTypedProviderConfig(new OllamaProviderConfig
{
    Temperature = 0.7f,
    NumPredict = 2048,
    NumCtx = 4096
});

var agent = await config.BuildAsync();
```

#### 4. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "OllamaAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "ollama",
        ModelName = "llama3:8b"
    }
};

var ollamaOpts = new OllamaProviderConfig
{
    Temperature = 0.7f,
    NumCtx = 4096
};
config.Provider.SetTypedProviderConfig(ollamaOpts);

// Reuse with different runtime customizations
var agent1 = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .WithToolkit<MathToolkit>()
    .BuildAsync();

var agent2 = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .WithToolkit<FileToolkit>()
    .BuildAsync();
```

### Provider-Specific Options

The `OllamaProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Parameters

```csharp
configure: opts =>
{
    // Maximum tokens to generate (default: 128, -1 = infinite, -2 = fill context)
    opts.NumPredict = 2048;

    // Context window size (default: 2048)
    opts.NumCtx = 4096;
}
```

#### Sampling Parameters

```csharp
configure: opts =>
{
    // Temperature: 0.0 = deterministic, 2.0 = very creative (default: 0.8)
    opts.Temperature = 0.7f;

    // Top-P nucleus sampling (default: 0.9)
    opts.TopP = 0.95f;

    // Top-K sampling - limits to K most likely tokens (default: 40)
    opts.TopK = 50;

    // Min-P - minimum probability threshold (default: 0.0)
    opts.MinP = 0.05f;

    // Typical-P - locally typical sampling (default: 1.0)
    opts.TypicalP = 0.95f;

    // Tail-free sampling Z (default: 1.0)
    opts.TfsZ = 2.0f;
}
```

#### Repetition Control

```csharp
configure: opts =>
{
    // Penalize repetitions (default: 1.1)
    opts.RepeatPenalty = 1.2f;

    // Look-back window for repetitions (default: 64, -1 = num_ctx)
    opts.RepeatLastN = 128;

    // Presence penalty (default: 0.0, range: 0.0-2.0)
    opts.PresencePenalty = 0.5f;

    // Frequency penalty (default: 0.0, range: 0.0-2.0)
    opts.FrequencyPenalty = 0.5f;

    // Penalize newline tokens (default: true)
    opts.PenalizeNewline = false;
}
```

#### Determinism & Stop Sequences

```csharp
configure: opts =>
{
    // Random seed for reproducible output (default: 0)
    opts.Seed = 12345;

    // Stop sequences
    opts.Stop = new[] { "\n\n", "END", "---" };
}
```

#### Mirostat Sampling

Advanced sampling for controlling perplexity:

```csharp
configure: opts =>
{
    // Enable Mirostat: 0 = disabled, 1 = Mirostat, 2 = Mirostat 2.0
    opts.MiroStat = 2;

    // Mirostat learning rate (default: 0.1)
    opts.MiroStatEta = 0.15f;

    // Mirostat target entropy (default: 5.0)
    opts.MiroStatTau = 4.5f;
}
```

#### Context & Memory

```csharp
configure: opts =>
{
    // Tokens to keep from initial prompt (default: 4, -1 = all)
    opts.NumKeep = 10;
}
```

#### Performance & Hardware

```csharp
configure: opts =>
{
    // GPU layers to offload (default: platform-dependent)
    opts.NumGpu = 35;

    // Primary GPU for small tensors (default: 0)
    opts.MainGpu = 0;

    // Enable low VRAM mode (default: false)
    opts.LowVram = true;

    // Use FP16 for key/value cache (default: false)
    opts.F16kv = true;

    // Return logits for all tokens (default: false)
    opts.LogitsAll = true;
}
```

#### Threading & Batch Processing

```csharp
configure: opts =>
{
    // CPU threads to use (default: auto-detected)
    opts.NumThread = 8;

    // Prompt processing batch size (default: 512)
    opts.NumBatch = 1024;

    // GQA groups for specific models (e.g., 8 for llama2:70b)
    opts.NumGqa = 8;
}
```

#### Memory Management

```csharp
configure: opts =>
{
    // Use memory mapping (default: true)
    opts.UseMmap = false;

    // Lock model in memory (default: false)
    opts.UseMlock = true;

    // Enable NUMA support (default: false)
    opts.Numa = true;

    // Load only vocabulary, not weights (default: false)
    opts.VocabOnly = false;
}
```

#### Ollama-Specific Options

```csharp
configure: opts =>
{
    // How long to keep model loaded: "5m", "1h", "-1" = indefinite
    opts.KeepAlive = "10m";

    // Response format: "json" for JSON mode
    opts.Format = "json";

    // Custom prompt template (overrides Modelfile)
    opts.Template = "{{.System}}\nUser: {{.Prompt}}\nAssistant:";

    // Enable thinking for reasoning models: true/false or "high"/"medium"/"low"
    opts.Think = true;
}
```

## Connection & Endpoints

### Local Ollama (Default)

```csharp
// Uses http://localhost:11434
var agent = await new AgentBuilder()
    .WithOllama(model: "llama3:8b")
    .BuildAsync();
```

### Remote Ollama Server

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "mistral",
        endpoint: "http://gpu-server:11434")
    .BuildAsync();
```

### Environment Variable Configuration

```bash
export OLLAMA_ENDPOINT="http://localhost:11434"
# or
export OLLAMA_HOST="http://localhost:11434"
```

```csharp
// Automatically uses OLLAMA_ENDPOINT or OLLAMA_HOST
var agent = await new AgentBuilder()
    .WithOllama(model: "llama3:8b")
    .BuildAsync();
```

### Endpoint Priority

1. Explicit `endpoint` parameter in `WithOllama()`
2. `OLLAMA_ENDPOINT` environment variable
3. `OLLAMA_HOST` environment variable
4. Default: `http://localhost:11434`

## Supported Models

Ollama supports a wide variety of models. For the complete and up-to-date list, see:

**[Ollama Model Library](https://ollama.com/library)**

### Popular Model Families

- **Meta Llama** - `llama3:8b`, `llama3:70b`, `llama3.1`, `llama3.2`, `llama3.2-vision`
- **Mistral AI** - `mistral`, `mistral-nemo`, `mixtral`
- **Qwen** - `qwen3:4b`, `qwen3:32b`, `qwq` (reasoning)
- **DeepSeek** - `deepseek-r1:8b`, `deepseek-r1:70b` (reasoning), `deepseek-coder`
- **Microsoft Phi** - `phi4`, `phi4-reasoning`
- **Google Gemma** - `gemma2`, `gemma2:27b`
- **Code Models** - `codellama`, `deepseek-coder`, `qwen2.5-coder`
- **Vision Models** - `llava`, `bakllava`, `llama3.2-vision`

### Model Tags

Ollama uses tags to specify model variants:
```
model-name:size
```

**Examples:**
- `llama3:8b` - Llama 3 with 8 billion parameters
- `mistral:latest` - Latest Mistral model
- `qwen3:4b` - Qwen 3 with 4 billion parameters
- `deepseek-r1:70b` - DeepSeek R1 reasoning model, 70B

### Pulling Models

Before using a model, pull it with Ollama CLI:

```bash
# Pull a specific model
ollama pull llama3:8b

# List available models
ollama list

# Remove a model
ollama rm llama3:8b
```

## Advanced Features

### JSON Output Mode

Force the model to respond with valid JSON:

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "llama3:8b",
        configure: opts => opts.Format = "json")
    .BuildAsync();

var response = await agent.RunAsync(
    "List 3 programming languages with their release years in JSON format");
```

### Reasoning Models

Use models with thinking/reasoning capabilities:

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "deepseek-r1:8b",
        configure: opts =>
        {
            opts.Think = true; // Enable thinking mode
            opts.NumCtx = 8192; // Larger context for reasoning
            opts.Temperature = 0.6f;
        })
    .BuildAsync();

var response = await agent.RunAsync(@"
Solve this logic puzzle:
Three people have different jobs.
- Alice is not a Doctor
- Bob is not an Engineer
- The Doctor's name comes before the Engineer's alphabetically
Who has which job?");
```

### Deterministic Generation

Produce consistent outputs for the same input:

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "llama3:8b",
        configure: opts =>
        {
            opts.Seed = 12345; // Fixed seed
            opts.Temperature = 0.0f; // No randomness
        })
    .BuildAsync();

// Same input will always produce same output
var response1 = await agent.RunAsync("Count from 1 to 10");
var response2 = await agent.RunAsync("Count from 1 to 10");
// response1 == response2
```

### Performance Tuning for Large Models

Optimize for GPU-accelerated inference:

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "llama3:70b",
        configure: opts =>
        {
            // GPU configuration
            opts.NumGpu = 40; // Offload 40 layers to GPU
            opts.MainGpu = 0; // Primary GPU

            // CPU/Memory configuration
            opts.NumThread = 8; // 8 CPU threads
            opts.NumBatch = 512; // Batch size

            // Context size
            opts.NumCtx = 4096;
        })
    .BuildAsync();
```

### Keep Model Loaded

Control how long models stay in memory:

```csharp
configure: opts =>
{
    // Keep loaded for 30 minutes
    opts.KeepAlive = "30m";

    // Keep loaded indefinitely
    // opts.KeepAlive = "-1";

    // Unload immediately after use
    // opts.KeepAlive = "0";
}
```

### Vision Models

Use multimodal models with image understanding:

```csharp
var agent = await new AgentBuilder()
    .WithOllama(model: "llava")
    .BuildAsync();

// Vision capabilities require image input through message API
// (implementation depends on your image handling approach)
```

### Tool/Function Calling

Enable function calling for compatible models:

```csharp
public class WeatherToolkit
{
    [Function("Get current weather for a location")]
    public string GetWeather(string location)
    {
        return $"The weather in {location} is sunny, 72°F";
    }
}

var agent = await new AgentBuilder()
    .WithOllama(model: "llama3.1:8b") // Function calling support
    .WithToolkit<WeatherToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What's the weather in Seattle?");
```

**Note:** Tool calling support varies by model. Compatible models include:
- `llama3.1:8b` and larger
- `mistral-nemo`
- `qwen2.5:7b` and larger

## Error Handling

The Ollama provider includes intelligent error classification and automatic retry logic.

### Error Categories

| Category | HTTP Status | Retry Behavior | Examples |
|----------|-------------|----------------|----------|
| **ClientError** | 400, 404 | No retry | Model not found, invalid parameters |
| **Transient** | 503, Connection |  Retry with backoff | Model loading, connection errors |
| **ServerError** | 500-599 |  Retry with backoff | Internal server errors |
| **Timeout** | 408 |  Retry with backoff | Request timeout |

### Ollama-Specific Errors

The provider handles OllamaSharp-specific exceptions:

- **OllamaException** - General Ollama errors
- **ResponseError** - Invalid API responses
- **ModelDoesNotSupportToolsException** - Tool calling not supported by model

### Common Issues

#### Model Not Found (404)

**Problem:** Model doesn't exist locally

**Solution:**
```bash
ollama pull llama3:8b
ollama list  # Verify model is available
```

#### Model Loading (503)

**Problem:** Model is being loaded into memory

**Solution:** The provider automatically retries. First load may take 10-30 seconds.

#### Connection Refused

**Problem:** Ollama is not running

**Solution:**
```bash
# Start Ollama
ollama serve

# Or on macOS/Windows, start Ollama app
```

#### Out of Memory

**Problem:** Model too large for available RAM/VRAM

**Solution:**
```csharp
configure: opts =>
{
    opts.NumCtx = 2048; // Reduce context
    opts.LowVram = true; // Enable low VRAM mode
    opts.NumGpu = 20; // Reduce GPU layers
}
```

Or use a smaller model variant:
```csharp
// Instead of llama3:70b, use:
model: "llama3:8b"  // or "llama3:7b"
```

#### Tool Calling Not Working

**Problem:** `ModelDoesNotSupportToolsException`

**Solution:** Use a model with function calling support:
```csharp
//  Supported
model: "llama3.1:8b"
model: "mistral-nemo"
model: "qwen2.5:7b"

// Not supported
model: "llama3:8b"  // Llama 3.0 doesn't support tools
```

## Examples

### Example 1: Basic Chat with Llama

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Ollama;

var agent = await new AgentBuilder()
    .WithOllama(model: "llama3:8b")
    .BuildAsync();

var response = await agent.RunAsync("Explain quantum computing in simple terms.");
Console.WriteLine(response);
```

### Example 2: Creative Writing

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "mistral",
        configure: opts =>
        {
            opts.Temperature = 1.2f; // More creative
            opts.TopP = 0.95f;
            opts.RepeatPenalty = 1.2f; // Reduce repetition
        })
    .BuildAsync();

var story = await agent.RunAsync("Write a creative short story about a robot learning to paint");
```

### Example 3: Code Generation

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "qwen2.5-coder:7b",
        configure: opts =>
        {
            opts.Temperature = 0.2f; // Low for precise code
            opts.NumPredict = 2048;
        })
    .BuildAsync();

var code = await agent.RunAsync(@"
Write a Python function to calculate fibonacci numbers using dynamic programming.
Include docstring and type hints.");
```

### Example 4: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithOllama(model: "llama3:8b")
    .BuildAsync();

Console.Write("Response: ");
await foreach (var chunk in agent.RunAsync("Write a short story about AI"))
{
    Console.Write(chunk);
}
Console.WriteLine();
```

### Example 5: Multi-Model Deployment

```csharp
// Fast small model for simple queries
var quickAgent = await new AgentBuilder()
    .WithOllama(
        model: "qwen3:4b",
        configure: opts =>
        {
            opts.NumCtx = 4096;
            opts.NumPredict = 512;
        })
    .BuildAsync();

// Powerful large model for complex tasks
var powerAgent = await new AgentBuilder()
    .WithOllama(
        model: "llama3:70b",
        configure: opts =>
        {
            opts.NumCtx = 8192;
            opts.NumGpu = 40;
        })
    .BuildAsync();

// Route based on complexity
var response = complexity == "simple"
    ? await quickAgent.RunAsync(prompt)
    : await powerAgent.RunAsync(prompt);
```

### Example 6: JSON Structured Output

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "qwen3:4b",
        configure: opts =>
        {
            opts.Format = "json";
            opts.Temperature = 0.3f;
        })
    .BuildAsync();

var jsonResponse = await agent.RunAsync(@"
List 5 programming languages with their release years.
Format: {""languages"": [{""name"": ""..."", ""year"": 1990}]}");

// Parse the JSON response
var data = JsonSerializer.Deserialize<LanguageData>(jsonResponse);
```

### Example 7: Reasoning with DeepSeek-R1

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "deepseek-r1:8b",
        configure: opts =>
        {
            opts.Think = true; // Enable thinking/reasoning
            opts.NumCtx = 8192; // Large context for reasoning chains
            opts.Temperature = 0.6f;
        })
    .BuildAsync();

var response = await agent.RunAsync(@"
A farmer has 17 sheep, and all but 9 die. How many are left?
Think through this step-by-step.");
```

### Example 8: Remote Server with Custom Configuration

```csharp
var agent = await new AgentBuilder()
    .WithOllama(
        model: "mixtral",
        endpoint: "http://gpu-server:11434",
        configure: opts =>
        {
            opts.Temperature = 0.7f;
            opts.NumCtx = 8192;
            opts.NumPredict = 4096;
            opts.KeepAlive = "1h"; // Keep loaded for 1 hour
        })
    .BuildAsync();
```

## Troubleshooting

### "Model name is required"

**Problem:** Missing model name in configuration.

**Solution:**
```csharp
//  Correct
.WithOllama(model: "llama3:8b")

// Incorrect
.WithOllama(model: null)
```

### "Connection refused to localhost:11434"

**Problem:** Ollama is not running.

**Solution:**
```bash
# Check if Ollama is running
curl http://localhost:11434/api/tags

# If not, start Ollama
ollama serve  # Linux/CLI
# or launch Ollama desktop app on macOS/Windows
```

### "Model 'llama3:8b' not found"

**Problem:** Model not pulled locally.

**Solution:**
```bash
# Pull the model
ollama pull llama3:8b

# Verify it's available
ollama list

# Use the exact name shown in the list
```

### Slow First Response

**Problem:** Model is being loaded into memory.

**Solution:** This is normal. Keep the model loaded:
```csharp
configure: opts => opts.KeepAlive = "30m"  // Keep in memory
```

Or pre-load:
```bash
ollama run llama3:8b "test"  # Pre-loads the model
```

### "Temperature must be between 0 and 2"

**Problem:** Invalid temperature value.

**Solution:**
```csharp
//  Valid range for Ollama
opts.Temperature = 0.7f;  // 0.0 to 2.0

// Invalid
opts.Temperature = 3.0f;
```

### Model Using Too Much Memory

**Problem:** Model consuming too much RAM/VRAM.

**Solution:**
```csharp
configure: opts =>
{
    // Reduce context size
    opts.NumCtx = 2048;  // Instead of 4096+

    // Enable low VRAM mode
    opts.LowVram = true;

    // Reduce GPU layers
    opts.NumGpu = 20;  // Instead of 40
}
```

Or use a quantized/smaller model:
```bash
ollama pull llama3:8b  # Instead of llama3:70b
```

### Function Calling Returns Errors

**Problem:** Model doesn't support tool calling.

**Solution:** Ensure you're using a compatible model:
```csharp
//  These support function calling
model: "llama3.1:8b"   // Llama 3.1+
model: "mistral-nemo"
model: "qwen2.5:7b"

// These don't support function calling
model: "llama3:8b"     // Llama 3.0
model: "mistral:7b"    // Base Mistral
```

## ProviderOptionsJson Reference

When configuring via JSON file, all Ollama-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `OllamaProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "ollama",
        "modelName": "llama3:8b",
        "endpoint": "http://localhost:11434",
        "providerOptionsJson": "{\"temperature\":0.7,\"numPredict\":2048,\"numCtx\":4096,\"topK\":40,\"topP\":0.9,\"seed\":42,\"keepAlive\":\"5m\"}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `numPredict` | int | -2, -1, ≥ 1 | Max tokens to generate (-1=infinite, -2=fill context) |
| `numCtx` | int | ≥ 1 | Context window size (default: 2048) |
| `temperature` | float | — | Sampling randomness (default: 0.8) |
| `topP` | float | 0.0–1.0 | Nucleus sampling threshold (default: 0.9) |
| `topK` | int | ≥ 1 | Top-K sampling (default: 40) |
| `minP` | float | 0.0–1.0 | Min probability relative to most likely token (default: 0.0) |
| `typicalP` | float | 0.0–1.0 | Locally typical sampling (default: 1.0) |
| `tfsZ` | float | — | Tail-free sampling (default: 1.0, 1.0=disabled) |
| `repeatPenalty` | float | — | Repetition penalty (default: 1.1) |
| `repeatLastN` | int | -1, 0, ≥ 1 | Lookback window for repetition (-1=numCtx, default: 64) |
| `presencePenalty` | float | — | Presence penalty (default: 0.0) |
| `frequencyPenalty` | float | — | Frequency penalty (default: 0.0) |
| `penalizeNewline` | bool | — | Penalize newline tokens (default: true) |
| `seed` | int | any | Deterministic generation seed (default: 0) |
| `stop` | string[] | — | Stop sequences |
| `miroStat` | int | 0, 1, 2 | Mirostat sampling (0=disabled, default: 0) |
| `miroStatEta` | float | — | Mirostat learning rate (default: 0.1) |
| `miroStatTau` | float | — | Mirostat coherence/diversity balance (default: 5.0) |
| `numKeep` | int | -1, ≥ 0 | Tokens to keep from prompt (-1=all, default: 4) |
| `numGpu` | int | ≥ 0 | GPU layers (0=CPU only, default: auto) |
| `mainGpu` | int | ≥ 0 | Primary GPU index (default: 0) |
| `lowVram` | bool | — | Low VRAM mode (default: false) |
| `f16kv` | bool | — | F16 key/value cache (default: false) |
| `logitsAll` | bool | — | Return all token logits (default: false) |
| `numThread` | int | ≥ 1 | CPU threads (default: auto-detected) |
| `numBatch` | int | ≥ 1 | Prompt processing batch size (default: 512) |
| `numGqa` | int | ≥ 1 | GQA groups (required for some models, e.g. llama2:70b=8) |
| `useMmap` | bool | — | Memory-map model files (default: true) |
| `useMlock` | bool | — | Lock model in memory (default: false) |
| `numa` | bool | — | NUMA support (default: false) |
| `vocabOnly` | bool | — | Load vocabulary only (default: false) |
| `keepAlive` | string | — | Model keep-alive duration (e.g. `"5m"`, `"1h"`, `"-1"`) |
| `format` | string | `"json"` or schema | Response format override |
| `template` | string | — | Prompt template override |
| `think` | bool/string | `true`, `false`, `"high"`, `"medium"`, `"low"` | Enable thinking (reasoning models) |

---

## OllamaProviderConfig API Reference

### Core Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `NumPredict` | `int?` | ≥ -2 | 128 | Max tokens to generate (-1 = infinite, -2 = fill context) |
| `NumCtx` | `int?` | ≥ 1 | 2048 | Context window size |

### Sampling Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `Temperature` | `float?` | 0.0-2.0 | 0.8 | Sampling temperature (creativity) |
| `TopP` | `float?` | 0.0-1.0 | 0.9 | Nucleus sampling threshold |
| `TopK` | `int?` | ≥ 1 | 40 | Top-K sampling limit |
| `MinP` | `float?` | 0.0-1.0 | 0.0 | Minimum probability threshold |
| `TypicalP` | `float?` | 0.0-1.0 | 1.0 | Locally typical sampling |
| `TfsZ` | `float?` | ≥ 0.0 | 1.0 | Tail-free sampling |

### Repetition Control

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `RepeatPenalty` | `float?` | ≥ 0.0 | 1.1 | Penalize repetitions |
| `RepeatLastN` | `int?` | - | 64 | Look-back window (0 = disabled, -1 = num_ctx) |
| `PresencePenalty` | `float?` | 0.0-2.0 | 0.0 | Penalize token presence |
| `FrequencyPenalty` | `float?` | 0.0-2.0 | 0.0 | Penalize token frequency |
| `PenalizeNewline` | `bool?` | - | `true` | Penalize newline tokens |

### Determinism

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Seed` | `int?` | 0 | Random seed for reproducibility |
| `Stop` | `string[]?` | - | Stop sequences |

### Mirostat Sampling

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `MiroStat` | `int?` | 0-2 | 0 | Mirostat mode (0 = disabled, 1 = Mirostat, 2 = Mirostat 2.0) |
| `MiroStatEta` | `float?` | ≥ 0.0 | 0.1 | Mirostat learning rate |
| `MiroStatTau` | `float?` | ≥ 0.0 | 5.0 | Mirostat target entropy |

### Context & Memory

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NumKeep` | `int?` | 4 | Tokens to keep from prompt (-1 = all) |

### Performance & Hardware

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NumGpu` | `int?` | Platform-dependent | GPU layers to offload |
| `MainGpu` | `int?` | 0 | Primary GPU index |
| `LowVram` | `bool?` | `false` | Enable low VRAM mode |
| `F16kv` | `bool?` | `false` | Use FP16 for key/value cache |
| `LogitsAll` | `bool?` | `false` | Return logits for all tokens |

### Threading & Batch Processing

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NumThread` | `int?` | Auto-detected | CPU threads to use |
| `NumBatch` | `int?` | 512 | Prompt processing batch size |
| `NumGqa` | `int?` | - | GQA groups (model-specific) |

### Memory Management

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseMmap` | `bool?` | `true` | Use memory mapping |
| `UseMlock` | `bool?` | `false` | Lock model in memory |
| `Numa` | `bool?` | `false` | Enable NUMA support |
| `VocabOnly` | `bool?` | `false` | Load only vocabulary |

### Ollama-Specific Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `KeepAlive` | `string?` | - | How long to keep model loaded ("5m", "1h", "-1") |
| `Format` | `string?` | - | Response format ("json" for JSON mode) |
| `Template` | `string?` | - | Custom prompt template |
| `Think` | `object?` | - | Enable thinking (true/false or "high"/"medium"/"low") |

### Advanced Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AdditionalProperties` | `Dictionary<string, object>?` | - | Custom parameters |

## Additional Resources

- [Ollama Official Site](https://ollama.com/)
- [Ollama Model Library](https://ollama.com/library)
- [Ollama GitHub](https://github.com/ollama/ollama)
- [OllamaSharp Library](https://github.com/awaescher/OllamaSharp)
- [Ollama API Documentation](https://github.com/ollama/ollama/blob/main/docs/api.md)
- [Ollama Modelfile Documentation](https://github.com/ollama/ollama/blob/main/docs/modelfile.md)

# ONNX Runtime GenAI Provider

**Provider Key:** `onnx-runtime`

## Overview

The ONNX Runtime GenAI provider enables HPD-Agent to run foundation models locally using Microsoft's ONNX Runtime GenAI library. This provider supports high-performance local inference with various hardware accelerators, making it ideal for privacy-sensitive applications, offline deployments, and cost-effective AI solutions.

**Key Features:**
-  Local model inference (no cloud API required)
-  Multiple hardware accelerators (CPU, CUDA, DirectML, QNN, OpenVINO, TensorRT, WebGPU)
-  Streaming support for real-time responses
-  Vision support (Phi Vision and other multi-modal models)
-  Multiple model architectures (Llama, Mistral, Phi, Gemma, Qwen, and more)
-  Multi-LoRA adapter support for model customization
-  Constrained decoding (JSON schema, grammar, regex)
-  Deterministic generation with seed control
-  Prompt caching for improved performance
-  Beam search and advanced sampling strategies

**For detailed API documentation, see:**
- [**OnnxRuntimeProviderConfig API Reference**](#onnxruntimeproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Providers.OnnxRuntime;

var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/phi-3-mini-4k-instruct-onnx")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.OnnxRuntime
```

**Dependencies:**
- `Microsoft.ML.OnnxRuntimeGenAI` - ONNX Runtime GenAI library
- `Microsoft.Extensions.AI` - AI abstractions

## Configuration

### Configuration Patterns

The ONNX Runtime provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.MaxLength = 2048;
            opts.Temperature = 0.7f;
            opts.DoSample = true;
            opts.TopP = 0.9f;
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`onnx-config.json`:**
```json
{
    "name": "OnnxAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "onnx-runtime",
        "modelName": "phi-3-mini",
        "providerOptionsJson": "{\"modelPath\":\"/path/to/model\",\"maxLength\":2048,\"temperature\":0.7,\"doSample\":true}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("onnx-config.json");
```

The `providerOptionsJson` value is a JSON string containing ONNX Runtime-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "OnnxAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "onnx-runtime",
        ModelName = "phi-3-mini"
    }
};

config.Provider.SetTypedProviderConfig(new OnnxRuntimeProviderConfig
{
    ModelPath = "/path/to/model",
    MaxLength = 2048,
    Temperature = 0.7f,
    DoSample = true
});

var agent = await config.BuildAsync();
```

#### 4. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "OnnxAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "onnx-runtime",
        ModelName = "phi-3-mini"
    }
};

var onnxOpts = new OnnxRuntimeProviderConfig
{
    ModelPath = "/path/to/phi-3-mini",
    MaxLength = 2048,
    Temperature = 0.7f
};
config.Provider.SetTypedProviderConfig(onnxOpts);

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

The `OnnxRuntimeProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Generation Parameters

```csharp
configure: opts =>
{
    // Maximum tokens to generate
    opts.MaxLength = 2048;

    // Minimum tokens to generate
    opts.MinLength = 10;

    // Batch size for parallel generation
    opts.BatchSize = 1;
}
```

#### Sampling Parameters

```csharp
configure: opts =>
{
    // Enable randomized sampling (false = greedy decoding)
    opts.DoSample = true;

    // Sampling temperature (0.0-2.0, higher = more random)
    opts.Temperature = 0.8f;

    // Top-K sampling (keep top K tokens)
    opts.TopK = 50;

    // Top-P nucleus sampling (0.0-1.0)
    opts.TopP = 0.9f;
}
```

#### Repetition Control

```csharp
configure: opts =>
{
    // Repetition penalty (> 1.0 penalizes repetition)
    opts.RepetitionPenalty = 1.1f;

    // Prevent n-gram repetition (size of n-grams)
    opts.NoRepeatNgramSize = 3;
}
```

#### Beam Search Parameters

```csharp
configure: opts =>
{
    // Number of beams for beam search (1 = greedy)
    opts.NumBeams = 4;

    // Number of sequences to return
    opts.NumReturnSequences = 2;

    // Stop when num_beams sentences are finished
    opts.EarlyStopping = true;

    // Length penalty (> 1.0 promotes longer sequences)
    opts.LengthPenalty = 1.2f;

    // Diversity penalty for beam groups
    opts.DiversityPenalty = 0.5f;
}
```

#### Determinism & Randomness

```csharp
configure: opts =>
{
    // Seed for deterministic output (-1 = random)
    opts.RandomSeed = 42;
}
```

#### Stop Sequences

```csharp
configure: opts =>
{
    // Custom stop sequences
    opts.StopSequences = new List<string>
    {
        "<|end|>",
        "<|user|>",
        "<|system|>"
    };
}
```

#### Performance Optimization

```csharp
configure: opts =>
{
    // Share past/present KV buffers (CUDA only)
    opts.PastPresentShareBuffer = true;

    // Chunk size for prefill chunking
    opts.ChunkSize = 512;

    // Enable conversation caching
    opts.EnableCaching = true;
}
```

#### Constrained Decoding

```csharp
configure: opts =>
{
    // Guidance type: "json", "grammar", "regex"
    opts.GuidanceType = "json";

    // JSON schema, grammar specification, or regex pattern
    opts.GuidanceData = @"{
        ""type"": ""object"",
        ""properties"": {
            ""name"": { ""type"": ""string"" },
            ""age"": { ""type"": ""number"" }
        },
        ""required"": [""name"", ""age""]
    }";

    // Enable fast-forward tokens for guidance
    opts.GuidanceEnableFFTokens = true;
}
```

#### Execution Provider Configuration

```csharp
configure: opts =>
{
    // Execution providers (order matters - tried in sequence)
    opts.Providers = new List<string> { "cuda", "cpu" };

    // Provider-specific options
    opts.ProviderOptions = new Dictionary<string, Dictionary<string, string>>
    {
        ["cuda"] = new Dictionary<string, string>
        {
            ["device_id"] = "0",
            ["cudnn_conv_algo_search"] = "DEFAULT",
            ["gpu_mem_limit"] = "2147483648" // 2GB
        },
        ["cpu"] = new Dictionary<string, string>
        {
            ["intra_op_num_threads"] = "8"
        }
    };
}
```

#### Multi-LoRA Adapter Support

```csharp
configure: opts =>
{
    // Path to adapter file
    opts.AdapterPath = "/path/to/adapters.onnx_adapter";

    // Adapter name to activate
    opts.AdapterName = "math_adapter";
}
```

## Model Setup

### Downloading Models

ONNX Runtime GenAI requires models in ONNX format. You can:

1. **Download pre-converted models** from Hugging Face:
   ```bash
   # Example: Download Phi-3 Mini
   git clone https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx
   ```

2. **Convert models yourself** using the ONNX Runtime GenAI model builder:
   ```bash
   # Install model builder
   pip install onnxruntime-genai

   # Convert a model
   python -m onnxruntime_genai.models.builder \
       -m microsoft/Phi-3-mini-4k-instruct \
       -o ./phi-3-mini-onnx \
       -p int4 \
       -e cpu
   ```

### Model Directory Structure

A valid ONNX model directory must contain:

```
phi-3-mini-onnx/
├── genai_config.json          # Model configuration
├── model.onnx                  # Main model file
├── model.onnx.data (optional)  # Model weights
└── tokenizer files             # Tokenizer configuration
```

### Supported Models

ONNX Runtime GenAI supports a wide range of model architectures:

| Architecture | Examples | Notes |
|--------------|----------|-------|
| **Llama** | Llama 2, Llama 3, Llama 3.1, Llama 3.2 | Including instruct and chat variants |
| **Mistral** | Mistral 7B, Mixtral 8x7B | MoE models supported |
| **Phi** | Phi-2, Phi-3, Phi-3.5 | Microsoft small language models |
| **Gemma** | Gemma 2B, Gemma 7B | Google's open models |
| **Qwen** | Qwen 1.5, Qwen 2, Qwen 2.5 | Alibaba's multilingual models |
| **DeepSeek** | DeepSeek Coder, DeepSeek Math | Specialized models |
| **SmolLM** | SmolLM3 | Small, efficient models |
| **Vision** | Phi Vision, Qwen-VL | Multi-modal models |
| **Audio** | Whisper | Speech-to-text models |

### Hardware Requirements

| Hardware | Provider | Notes |
|----------|----------|-------|
| **CPU** | `cpu` | Works on all platforms, slower than GPU |
| **NVIDIA GPU** | `cuda` | Requires CUDA Toolkit 11.8+ |
| **AMD GPU** | `rocm` | Requires ROCm 5.4+ |
| **Intel GPU** | `openvino` | Requires OpenVINO 2023.0+ |
| **Windows GPU** | `dml` | DirectML, works with any GPU |
| **Qualcomm NPU** | `qnn` | Snapdragon processors |
| **WebGPU** | `webgpu` | Browser-based inference |
| **TensorRT** | `trt` | NVIDIA GPUs, optimized |

## Advanced Features

### Hardware Acceleration

Configure execution providers for optimal performance:

```csharp
// CUDA (NVIDIA GPU)
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.Providers = new List<string> { "cuda", "cpu" };
            opts.ProviderOptions = new Dictionary<string, Dictionary<string, string>>
            {
                ["cuda"] = new Dictionary<string, string>
                {
                    ["device_id"] = "0",
                    ["gpu_mem_limit"] = "4294967296" // 4GB
                }
            };
        })
    .BuildAsync();

// DirectML (Windows, any GPU)
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.Providers = new List<string> { "dml", "cpu" };
        })
    .BuildAsync();

// OpenVINO (Intel hardware)
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.Providers = new List<string> { "openvino", "cpu" };
        })
    .BuildAsync();
```

### Constrained Generation

Force the model to generate structured output:

#### JSON Schema Constraint

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.GuidanceType = "json";
            opts.GuidanceData = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"" },
                    ""age"": { ""type"": ""number"" },
                    ""email"": { ""type"": ""string"", ""format"": ""email"" }
                },
                ""required"": [""name"", ""age""]
            }";
        })
    .BuildAsync();

var response = await agent.RunAsync("Extract user info: John Doe, 30 years old, john@example.com");
// Guaranteed to return valid JSON matching the schema
```

#### Grammar Constraint

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.GuidanceType = "grammar";
            opts.GuidanceData = @"
                root ::= sentence+
                sentence ::= noun verb noun '.'
                noun ::= 'cat' | 'dog' | 'bird'
                verb ::= 'chases' | 'loves' | 'sees'
            ";
        })
    .BuildAsync();
```

### Multi-LoRA Adapters

Use LoRA adapters to customize base models:

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/base-model",
        configure: opts =>
        {
            opts.AdapterPath = "/path/to/adapters.onnx_adapter";
            opts.AdapterName = "math_specialist";
        })
    .BuildAsync();

var response = await agent.RunAsync("Solve: 2x + 5 = 15");
```

### Deterministic Generation

Generate reproducible outputs:

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.RandomSeed = 42;
            opts.DoSample = true;
            opts.Temperature = 0.7f;
        })
    .BuildAsync();

// Same input + same seed = same output every time
var response1 = await agent.RunAsync("Generate a random story");
var response2 = await agent.RunAsync("Generate a random story");
// response1 == response2
```

### Beam Search

Explore multiple generation paths:

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.NumBeams = 5;
            opts.NumReturnSequences = 3;
            opts.EarlyStopping = true;
            opts.LengthPenalty = 1.2f;
        })
    .BuildAsync();
```

## Error Handling

The ONNX Runtime provider includes intelligent error classification and retry logic.

### Error Categories

| Category | Retry Behavior | Examples |
|----------|----------------|----------|
| **ClientError** | No retry | Invalid configuration, model not found, corrupted model |
| **Transient** |  Exponential backoff | Out of memory, device busy, timeout |

### Common Exceptions

#### Model Loading Errors

```
OnnxRuntimeGenAIException: Failed to load model
```
**Solution:** Verify model path and ensure all required files exist

#### Out of Memory

```
OnnxRuntimeGenAIException: Out of memory
```
**Solution:**
- Reduce `MaxLength` or `BatchSize`
- Use quantized model (int4, int8)
- Set GPU memory limit in provider options

#### Invalid Configuration

```
OnnxRuntimeGenAIException: Invalid parameter
```
**Solution:** Check parameter ranges (e.g., temperature 0-2, topP 0-1)

#### Provider Not Available

```
OnnxRuntimeGenAIException: Provider 'cuda' not available
```
**Solution:** Verify CUDA/GPU drivers installed, or fall back to CPU

## Examples

### Example 1: Basic Local Chat

```csharp
using HPD.Agent;
using HPD.Agent.Providers.OnnxRuntime;

var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/phi-3-mini-4k-instruct-onnx")
    .BuildAsync();

var response = await agent.RunAsync("Explain quantum computing in simple terms.");
Console.WriteLine(response);
```

### Example 2: GPU-Accelerated Generation

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/llama-3-8b-instruct-onnx",
        configure: opts =>
        {
            opts.Providers = new List<string> { "cuda", "cpu" };
            opts.MaxLength = 4096;
            opts.Temperature = 0.7f;
            opts.DoSample = true;
        })
    .BuildAsync();

var response = await agent.RunAsync("Write a detailed essay about artificial intelligence.");
```

### Example 3: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model")
    .BuildAsync();

await foreach (var chunk in agent.RunAsync("Write a short story about space exploration."))
{
    Console.Write(chunk);
}
```

### Example 4: Structured JSON Output

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.GuidanceType = "json";
            opts.GuidanceData = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""product"": { ""type"": ""string"" },
                    ""price"": { ""type"": ""number"" },
                    ""inStock"": { ""type"": ""boolean"" }
                },
                ""required"": [""product"", ""price"", ""inStock""]
            }";
        })
    .BuildAsync();

var response = await agent.RunAsync("Extract product info: MacBook Pro for $2499, available now");
// Returns: {"product":"MacBook Pro","price":2499,"inStock":true}
```

### Example 5: Deterministic Sampling

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.DoSample = true;
            opts.TopK = 50;
            opts.TopP = 0.9f;
            opts.Temperature = 0.8f;
            opts.RandomSeed = 12345;
            opts.RepetitionPenalty = 1.1f;
        })
    .BuildAsync();

// Reproducible creative generation
var story1 = await agent.RunAsync("Generate a creative story about robots.");
var story2 = await agent.RunAsync("Generate a creative story about robots.");
// story1 == story2 (deterministic due to seed)
```

### Example 6: Multi-LoRA Adapter Switching

```csharp
// Math-specialized agent
var mathAgent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/base-model",
        configure: opts =>
        {
            opts.AdapterPath = "/path/to/adapters.onnx_adapter";
            opts.AdapterName = "math_adapter";
        })
    .BuildAsync();

// Code-specialized agent (same base model, different adapter)
var codeAgent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/base-model",
        configure: opts =>
        {
            opts.AdapterPath = "/path/to/adapters.onnx_adapter";
            opts.AdapterName = "code_adapter";
        })
    .BuildAsync();
```

### Example 7: Beam Search for Quality

```csharp
var agent = await new AgentBuilder()
    .WithOnnxRuntime(
        modelPath: "/path/to/model",
        configure: opts =>
        {
            opts.NumBeams = 4;
            opts.NumReturnSequences = 1;
            opts.EarlyStopping = true;
            opts.LengthPenalty = 1.2f;
            opts.NoRepeatNgramSize = 3;
        })
    .BuildAsync();

var response = await agent.RunAsync("Translate to French: The weather is beautiful today.");
// Higher quality translation due to beam search
```

## Troubleshooting

### "Model path does not exist"

**Problem:** Invalid or missing model path.

**Solution:**
```csharp
// Verify path exists
if (!Directory.Exists(modelPath))
{
    throw new DirectoryNotFoundException($"Model not found: {modelPath}");
}

// Use absolute path
.WithOnnxRuntime(modelPath: Path.GetFullPath("/path/to/model"))

// Or use environment variable
Environment.SetEnvironmentVariable("ONNX_MODEL_PATH", "/path/to/model");
.WithOnnxRuntime(modelPath: Environment.GetEnvironmentVariable("ONNX_MODEL_PATH"))
```

### "Out of memory" errors

**Problem:** Model too large for available memory.

**Solution:**
```csharp
configure: opts =>
{
    // Reduce max length
    opts.MaxLength = 1024; // Instead of 4096

    // Set GPU memory limit
    opts.ProviderOptions = new Dictionary<string, Dictionary<string, string>>
    {
        ["cuda"] = new Dictionary<string, string>
        {
            ["gpu_mem_limit"] = "2147483648" // 2GB
        }
    };

    // Use quantized model (int4 instead of fp16)
    // Download int4 quantized version of your model
}
```

### "Provider 'cuda' not available"

**Problem:** CUDA provider requested but not installed.

**Solution:**
```csharp
// Option 1: Fall back to CPU
configure: opts =>
{
    opts.Providers = new List<string> { "cuda", "cpu" }; // Tries CUDA first, falls back to CPU
}

// Option 2: Check CUDA installation
// Verify: nvidia-smi command works
// Install: CUDA Toolkit 11.8 or later

// Option 3: Use DirectML (Windows) instead
configure: opts =>
{
    opts.Providers = new List<string> { "dml", "cpu" };
}
```

### "Temperature must be between 0 and 2"

**Problem:** Invalid temperature value.

**Solution:**
```csharp
configure: opts =>
{
    opts.Temperature = 0.7f;  //  Valid (0.0-2.0)
    // NOT: opts.Temperature = 2.5f;  // Invalid for ONNX Runtime
}
```

### Model loading is very slow

**Problem:** Model loading from disk is slow.

**Solution:**
```csharp
// Option 1: Use SSD instead of HDD for model storage
// Option 2: Enable caching
configure: opts =>
{
    opts.EnableCaching = true;
}

// Option 3: Load model once and reuse agent instance
// Don't create new agents for each request
```

### Generation is very slow

**Problem:** Slow inference speed.

**Solution:**
```csharp
// Option 1: Use GPU acceleration
configure: opts =>
{
    opts.Providers = new List<string> { "cuda" };
}

// Option 2: Use quantized model (int4/int8)
// Download quantized version: significantly faster

// Option 3: Reduce max length
configure: opts =>
{
    opts.MaxLength = 512; // Instead of 2048
}

// Option 4: Enable performance optimizations
configure: opts =>
{
    opts.PastPresentShareBuffer = true; // CUDA only
    opts.ChunkSize = 512; // Enable chunking
}
```

## ProviderOptionsJson Reference

When configuring via JSON file, all ONNX Runtime-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `OnnxRuntimeProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "onnx-runtime",
        "modelName": "phi-3-mini",
        "providerOptionsJson": "{\"modelPath\":\"/path/to/model\",\"maxLength\":2048,\"temperature\":0.7,\"doSample\":true,\"topK\":50,\"topP\":0.9,\"randomSeed\":42}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `modelPath` | string | — | **Required.** Path to ONNX model directory |
| `maxLength` | int | ≥ 1 | Max sequence length (0=model.context_length) |
| `minLength` | int | ≥ 0 | Min sequence length |
| `batchSize` | int | ≥ 1 | Input batch size (default: 1) |
| `doSample` | bool | — | Use sampling (false=greedy decoding, default: false) |
| `temperature` | float | > 0.0 | Sampling randomness (default: 1.0, only when `doSample` is true) |
| `topK` | int | ≥ 1 | Top-K sampling (default: 50, only when `doSample` is true) |
| `topP` | float | 0.0–1.0 | Nucleus sampling (only when `doSample` is true) |
| `repetitionPenalty` | float | ≥ 1.0 | Repetition penalty (1.0=no penalty) |
| `noRepeatNgramSize` | int | ≥ 0 | Prevent n-gram repetition (0=disabled) |
| `numBeams` | int | ≥ 1 | Beam search width (1=greedy, default: 1) |
| `numReturnSequences` | int | ≥ 1 | Sequences to return from beam search (≤ numBeams) |
| `earlyStopping` | bool | — | Stop beam search early (default: true) |
| `lengthPenalty` | float | — | Beam length penalty (>1=longer, <1=shorter, default: 1.0) |
| `diversityPenalty` | float | ≥ 0.0 | Beam diversity penalty |
| `randomSeed` | int | -1 or any | RNG seed (-1=random, any other=deterministic, default: -1) |
| `stopSequences` | string[] | — | Stop generation sequences |
| `enableCaching` | bool | — | Enable conversation caching (default: false) |
| `guidanceType` | string | `"json"`, `"grammar"`, `"regex"` | Constrained decoding type |
| `guidanceData` | string | — | Schema/grammar/regex for constrained decoding |
| `guidanceEnableFFTokens` | bool | — | Fast-forward tokens for guidance (default: false) |
| `pastPresentShareBuffer` | bool | — | Share KV buffer (CUDA only, default: false) |
| `chunkSize` | int | ≥ 1 | Prefill chunk size (0=disabled) |
| `providers` | string[] | `"cuda"`, `"cpu"`, `"dml"`, `"qnn"`, etc. | Execution providers in priority order |
| `providerOptions` | object | — | Provider-specific options (`{ "cuda": { "device_id": "0" } }`) |
| `hardwareDeviceType` | string | `"cpu"`, `"gpu"`, `"npu"` | Decoder device type |
| `hardwareDeviceId` | uint | ≥ 0 | Decoder device ID |
| `hardwareVendorId` | uint | — | Decoder hardware vendor ID |
| `adapterPath` | string | — | LoRA adapter file path |
| `adapterName` | string | — | LoRA adapter name to activate |

---

## OnnxRuntimeProviderConfig API Reference

### Core Generation Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `ModelPath` | `string?` | - | Required | Path to ONNX model directory |
| `MaxLength` | `int?` | ≥ 1 | Model-specific | Maximum tokens to generate |
| `MinLength` | `int?` | ≥ 0 | 0 | Minimum tokens to generate |
| `BatchSize` | `int?` | ≥ 1 | 1 | Batch size for parallel generation |

### Sampling Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `DoSample` | `bool?` | - | `false` | Enable randomized sampling |
| `Temperature` | `float?` | 0.0-2.0 | 1.0 | Sampling temperature |
| `TopK` | `int?` | ≥ 1 | 50 | Top-K sampling |
| `TopP` | `float?` | 0.0-1.0 | - | Nucleus sampling threshold |

### Repetition Control

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `RepetitionPenalty` | `float?` | > 0 | 1.0 | Repetition penalty (1.0 = no penalty) |
| `NoRepeatNgramSize` | `int?` | ≥ 0 | - | Prevent n-gram repetition |

### Beam Search Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `NumBeams` | `int?` | ≥ 1 | 1 | Number of beams (1 = greedy) |
| `NumReturnSequences` | `int?` | ≥ 1, ≤ NumBeams | 1 | Sequences to return |
| `EarlyStopping` | `bool?` | - | `true` | Stop when num_beams finished |
| `LengthPenalty` | `float?` | > 0 | 1.0 | Length penalty for beam search |
| `DiversityPenalty` | `float?` | ≥ 0 | - | Diversity penalty for beams |

### Performance Optimization

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `PastPresentShareBuffer` | `bool?` | `false` | Share KV buffers (CUDA only) |
| `ChunkSize` | `int?` | - | Chunk size for prefill chunking |
| `EnableCaching` | `bool` | `false` | Enable conversation caching |

### Determinism & Randomness

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `RandomSeed` | `int?` | -1 or ≥ 0 | -1 | RNG seed (-1 = random) |

### Stop Sequences

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StopSequences` | `List<string>?` | - | Custom stop sequences |

### Constrained Decoding

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `GuidanceType` | `string?` | - | Guidance type: "json", "grammar", "regex" |
| `GuidanceData` | `string?` | - | Schema/grammar/pattern for guidance |
| `GuidanceEnableFFTokens` | `bool` | `false` | Enable fast-forward tokens |

### Execution Provider Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Providers` | `List<string>?` | - | Execution providers in priority order |
| `ProviderOptions` | `Dictionary<string, Dictionary<string, string>>?` | - | Provider-specific options |

### Multi-LoRA Adapters

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AdapterPath` | `string?` | - | Path to adapter file |
| `AdapterName` | `string?` | - | Adapter name to activate |

## Additional Resources

- [ONNX Runtime GenAI Documentation](https://onnxruntime.ai/docs/genai/)
- [ONNX Runtime GenAI GitHub](https://github.com/microsoft/onnxruntime-genai)
- [Model Builder Guide](https://onnxruntime.ai/docs/genai/howto/build-model.html)
- [Supported Models List](https://onnxruntime.ai/docs/genai/reference/supported-models.html)
- [Execution Providers](https://onnxruntime.ai/docs/execution-providers/)
- [Hugging Face ONNX Models](https://huggingface.co/models?library=onnx)

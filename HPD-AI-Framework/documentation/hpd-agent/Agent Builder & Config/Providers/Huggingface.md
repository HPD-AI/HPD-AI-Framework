# HuggingFace Provider

**Provider Key:** `huggingface`

## Overview

The HuggingFace provider enables HPD-Agent to use thousands of open-source language models hosted on HuggingFace's Serverless Inference API. Access state-of-the-art models including LLaMA, Mistral, CodeLLaMA, StarCoder, and many others through a simple, unified interface.

**Key Features:**
-  Access to thousands of open-source models
-  Streaming support for real-time responses
-  Free tier available (rate-limited)
-  No function calling (HuggingFace Inference API limitation)
-  No vision support (text generation only)
-  Automatic model loading and caching
-  Simple token-based authentication
-  Multi-target framework support (net8.0, net9.0, net10.0)
-  AOT-compatible (Native AOT ready)

**For detailed API documentation, see:**
- [**HuggingFaceProviderConfig API Reference**](#huggingfaceproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Providers.HuggingFace;

// Set HuggingFace API token via environment variable
Environment.SetEnvironmentVariable("HF_TOKEN", "hf_...");

var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-8B-Instruct")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.HuggingFace
```

**Dependencies:**
- `HuggingFace` - HuggingFace Serverless Inference API SDK
- `Microsoft.Extensions.AI` - AI abstractions

## Configuration

### Configuration Patterns

The HuggingFace provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-8B-Instruct",
        apiKey: "hf_...",
        configure: opts =>
        {
            opts.MaxNewTokens = 500;
            opts.Temperature = 0.7;
            opts.TopP = 0.9;
            opts.RepetitionPenalty = 1.1;
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`huggingface-config.json`:**
```json
{
    "name": "HuggingFaceAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "huggingface",
        "modelName": "meta-llama/Meta-Llama-3-8B-Instruct",
        "apiKey": "hf_...",
        "providerOptionsJson": "{\"maxNewTokens\":250,\"temperature\":0.7,\"topP\":0.9}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("huggingface-config.json");
```

The `providerOptionsJson` value is a JSON string containing HuggingFace-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "HuggingFaceAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "huggingface",
        ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
        ApiKey = "hf_..."
    }
};

config.Provider.SetTypedProviderConfig(new HuggingFaceProviderConfig
{
    MaxNewTokens = 250,
    Temperature = 0.7,
    TopP = 0.9
});

var agent = await config.BuildAsync();
```

#### 4. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "HuggingFaceAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "huggingface",
        ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
        ApiKey = "hf_..."
    }
};

var hfOpts = new HuggingFaceProviderConfig
{
    MaxNewTokens = 500,
    Temperature = 0.7
};
config.Provider.SetTypedProviderConfig(hfOpts);

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

The `HuggingFaceProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Parameters

```csharp
configure: opts =>
{
    // Maximum new tokens to generate (default: 250)
    opts.MaxNewTokens = 500;

    // Sampling temperature (0.0-100.0, default: 1.0)
    // 1.0 = regular sampling, 0.0 = greedy, higher = more random
    opts.Temperature = 0.7;

    // Top-P nucleus sampling (0.0-1.0)
    opts.TopP = 0.9;

    // Top-K sampling (positive integer)
    opts.TopK = 50;
}
```

#### Sampling Control

```csharp
configure: opts =>
{
    // Repetition penalty (≥ 0, default: 1.0)
    // Higher values discourage repetition
    opts.RepetitionPenalty = 1.1;

    // Whether to use sampling (default: true)
    // False = greedy decoding
    opts.DoSample = true;

    // Number of sequences to return (default: 1)
    opts.NumReturnSequences = 1;
}
```

#### Generation Control

```csharp
configure: opts =>
{
    // Include input in output (default: true)
    // Set to false for cleaner prompting
    opts.ReturnFullText = false;

    // Maximum generation time in seconds (soft limit)
    opts.MaxTime = 30.0;
}
```

#### API Options

```csharp
configure: opts =>
{
    // Use HuggingFace's caching layer (default: true)
    // Set to false for non-deterministic models
    opts.UseCache = false;

    // Wait for model to load instead of 503 error (default: false)
    // Useful after receiving a 503
    opts.WaitForModel = true;
}
```

#### Advanced Options

```csharp
configure: opts =>
{
    // Additional custom parameters for specific models
    opts.AdditionalProperties = new Dictionary<string, object>
    {
        ["customField"] = "value"
    };
}
```

## Authentication

HuggingFace uses simple token-based authentication. You need a HuggingFace API token (HF_TOKEN) which you can get from [huggingface.co/settings/tokens](https://huggingface.co/settings/tokens).

### Authentication Priority Order

1. **Explicit `apiKey` parameter** in `WithHuggingFace()`
2. **Environment variable**: `HF_TOKEN`
3. **Environment variable**: `HUGGINGFACE_API_KEY`
4. **Configuration file**: `appsettings.json` → `"huggingface:ApiKey"` or `"HuggingFace:ApiKey"`

### Method 1: Environment Variable (Recommended for Development)

```bash
export HF_TOKEN="hf_..."
```

```csharp
// Automatically uses HF_TOKEN environment variable
var agent = await new AgentBuilder()
    .WithHuggingFace(model: "meta-llama/Meta-Llama-3-8B-Instruct")
    .BuildAsync();
```

### Method 2: Explicit API Key

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-8B-Instruct",
        apiKey: "hf_...")
    .BuildAsync();
```

**Security Warning:** Never hardcode API keys in source code. Use environment variables or configuration files instead.

### Method 3: Configuration File

**appsettings.json:**
```json
{
    "HuggingFace": {
        "ApiKey": "hf_..."
    }
}
```

```csharp
// Automatically resolved from configuration
var agent = await new AgentBuilder()
    .WithHuggingFace(model: "meta-llama/Meta-Llama-3-8B-Instruct")
    .BuildAsync();
```

### Getting Your API Token

1. Sign up at [huggingface.co](https://huggingface.co)
2. Go to [Settings → Access Tokens](https://huggingface.co/settings/tokens)
3. Create a new token with "Read" access
4. Copy the token (starts with `hf_`)

## Supported Models

HuggingFace provides access to thousands of open-source models. For the complete list, visit [huggingface.co/models](https://huggingface.co/models?pipeline_tag=text-generation&sort=trending).

### Popular Model Families

- **Meta LLaMA** - Advanced reasoning and instruction following
  - `meta-llama/Meta-Llama-3-8B-Instruct`
  - `meta-llama/Meta-Llama-3-70B-Instruct`
  - `meta-llama/Llama-2-7b-chat-hf`

- **Mistral AI** - Efficient multilingual models
  - `mistralai/Mistral-7B-Instruct-v0.2`
  - `mistralai/Mixtral-8x7B-Instruct-v0.1`

- **Code Models** - Specialized for code generation
  - `bigcode/starcoder2-15b`
  - `codellama/CodeLlama-13b-Instruct-hf`

- **Small Models** - Fast inference
  - `microsoft/phi-2`
  - `TinyLlama/TinyLlama-1.1B-Chat-v1.0`

### Model Repository ID Format

HuggingFace model IDs follow this pattern:
```
organization/model-name
```

**Examples:**
- `meta-llama/Meta-Llama-3-8B-Instruct`
- `mistralai/Mistral-7B-Instruct-v0.2`
- `bigcode/starcoder2-15b`

💡 **Tip:** Look for models with `-Instruct` or `-Chat` in the name for best conversational results.

## Advanced Features

### Streaming Responses

Get real-time token-by-token responses:

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(model: "meta-llama/Meta-Llama-3-8B-Instruct")
    .BuildAsync();

await foreach (var chunk in agent.RunAsync("Write a story about AI."))
{
    Console.Write(chunk);
}
```

### Model Loading Behavior

HuggingFace models may need time to load if they haven't been used recently:

```csharp
configure: opts =>
{
    // Wait for model to load instead of getting 503 error
    opts.WaitForModel = true;

    // Set max time to wait
    opts.MaxTime = 60.0; // 1 minute
}
```

### Non-Deterministic Models

For models that use randomness:

```csharp
configure: opts =>
{
    // Disable caching to get fresh results each time
    opts.UseCache = false;

    // Enable sampling
    opts.DoSample = true;
    opts.Temperature = 0.8;
}
```

### Code Generation Models

Optimize settings for code generation:

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "bigcode/starcoder2-15b",
        configure: opts =>
        {
            opts.MaxNewTokens = 1000;
            opts.Temperature = 0.2; // Lower for more deterministic code
            opts.TopP = 0.95;
            opts.RepetitionPenalty = 1.1;
            opts.ReturnFullText = false;
        })
    .BuildAsync();
```

### Long-Form Generation

For generating longer outputs:

```csharp
configure: opts =>
{
    opts.MaxNewTokens = 2000; // Increase token limit
    opts.MaxTime = 45.0; // Allow more time
    opts.Temperature = 0.7;
    opts.RepetitionPenalty = 1.15; // Reduce repetition
}
```

## Error Handling

The HuggingFace provider includes intelligent error classification and automatic retry logic.

### Error Categories

| Category | HTTP Status | Retry Behavior | Examples |
|----------|-------------|----------------|----------|
| **AuthError** | 401, 403 | No retry | Invalid or missing API token |
| **RateLimitRetryable** | 429 |  Exponential backoff | Rate limit exceeded, quota hit |
| **ClientError** | 400, 404, 413 | No retry | Invalid parameters, model not found, payload too large |
| **Transient** | 503 |  Retry | Model loading, service unavailable |
| **ServerError** | 500-599 |  Retry | Internal server error |

### Automatic Retry Configuration

The provider automatically retries transient errors and rate limits with exponential backoff:

```csharp
// Retry behavior is built-in and automatic
// No configuration needed!
```

**Default Retry Behavior:**
- Initial delay: 1 second
- Multiplier: 2x (exponential backoff)
- Max delay: 30 seconds
- Retries: Rate limits, server errors, transient errors

### Common Exceptions

#### RateLimitException (429)
```
Rate limit exceeded - automatic exponential backoff retry
```
**Cause:** Too many requests

**Solutions:**
- Wait for rate limit to reset (handled automatically)
- Upgrade to HuggingFace Pro for higher limits
- Reduce request frequency

#### AuthenticationError (401/403)
```
Unauthorized or forbidden access
```
**Cause:** Invalid, missing, or insufficient API token

**Solutions:**
- Verify API token is correct
- Regenerate token if expired
- Check token has "Read" permission

#### ModelNotFoundError (404)
```
Model not found
```
**Cause:** Invalid model repository ID

**Solutions:**
- Check model ID spelling: `organization/model-name`
- Verify model exists on HuggingFace Hub
- Ensure model supports text generation

#### ValidationError (400)
```
Invalid request parameters
```
**Cause:** Parameter out of valid range

**Solutions:**
- Check parameter ranges (see API Reference)
- Verify temperature is 0-100
- Ensure TopP is 0-1

#### ModelLoadingError (503)
```
Model is currently loading
```
**Cause:** Model needs time to load into memory

**Solutions:**
- Set `WaitForModel = true` to wait automatically
- Retry after a few seconds (handled automatically)
- Use more popular models (faster loading)

## Limitations

### No Function/Tool Calling

The HuggingFace Serverless Inference API does not support function/tool calling.

**Workaround:** If you need tool calling, consider:
- Using a different provider (Anthropic, OpenAI, Azure AI)
- Implementing manual tool extraction from model output
- Using HuggingFace's Text Generation Inference (self-hosted)

### No Vision Support

Currently limited to text generation only. Vision models are not supported in this provider.

### No Structured Output

The provider does not support JSON schema or structured output modes.

**Workaround:**
- Use prompt engineering to request JSON output
- Parse model output manually

### Rate Limits

Free tier has rate limits:
- **Free tier**: ~1000 requests/day per model
- **Pro tier**: Higher limits (see HuggingFace pricing)

## Examples

### Example 1: Basic Chat

```csharp
using HPD.Agent;
using HPD.Agent.Providers.HuggingFace;

var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-8B-Instruct",
        apiKey: "hf_...")
    .BuildAsync();

var response = await agent.RunAsync("Explain quantum computing in simple terms.");
Console.WriteLine(response);
```

### Example 2: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(model: "mistralai/Mistral-7B-Instruct-v0.2")
    .BuildAsync();

Console.Write("Response: ");
await foreach (var chunk in agent.RunAsync("Write a haiku about AI."))
{
    Console.Write(chunk);
}
Console.WriteLine();
```

### Example 3: Code Generation

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "bigcode/starcoder2-15b",
        configure: opts =>
        {
            opts.MaxNewTokens = 1000;
            opts.Temperature = 0.2;
            opts.TopP = 0.95;
            opts.ReturnFullText = false;
        })
    .BuildAsync();

var code = await agent.RunAsync(
    "Write a Python function to calculate fibonacci numbers.");
Console.WriteLine(code);
```

### Example 4: Creative Writing

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-8B-Instruct",
        configure: opts =>
        {
            opts.MaxNewTokens = 2000;
            opts.Temperature = 0.8; // Higher for creativity
            opts.TopP = 0.95;
            opts.RepetitionPenalty = 1.2;
            opts.UseCache = false; // Get varied results
        })
    .BuildAsync();

var story = await agent.RunAsync(
    "Write a short science fiction story about time travel.");
Console.WriteLine(story);
```

### Example 5: Model Comparison

```csharp
var models = new[]
{
    "meta-llama/Meta-Llama-3-8B-Instruct",
    "mistralai/Mistral-7B-Instruct-v0.2",
    "microsoft/phi-2"
};

var prompt = "What is artificial intelligence?";

foreach (var model in models)
{
    var agent = await new AgentBuilder()
        .WithHuggingFace(model: model)
        .BuildAsync();

    Console.WriteLine($"\n=== {model} ===");
    var response = await agent.RunAsync(prompt);
    Console.WriteLine(response);
}
```

### Example 6: Multi-Turn Conversation

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-8B-Instruct",
        configure: opts =>
        {
            opts.MaxNewTokens = 500;
            opts.Temperature = 0.7;
        })
    .BuildAsync();

// First turn
var response1 = await agent.RunAsync("What is machine learning?");
Console.WriteLine($"Agent: {response1}\n");

// Follow-up (maintains context)
var response2 = await agent.RunAsync("Can you give me an example?");
Console.WriteLine($"Agent: {response2}");
```

### Example 7: Wait for Model Loading

```csharp
var agent = await new AgentBuilder()
    .WithHuggingFace(
        model: "meta-llama/Meta-Llama-3-70B-Instruct", // Large model
        configure: opts =>
        {
            opts.WaitForModel = true; // Wait instead of 503 error
            opts.MaxTime = 60.0; // Allow up to 60 seconds
        })
    .BuildAsync();

var response = await agent.RunAsync("Hello!");
Console.WriteLine(response);
```

## Troubleshooting

### "API key (HF_TOKEN) is required"

**Problem:** Missing HuggingFace API token.

**Solution:**
```bash
# Set environment variable
export HF_TOKEN="hf_your_token_here"
```

Or provide explicitly:
```csharp
.WithHuggingFace(model: "...", apiKey: "hf_...")
```

### "Model name (repository ID) is required"

**Problem:** Model repository ID not specified.

**Solution:**
```csharp
// Wrong: No model specified
.WithHuggingFace()

//  Correct: Specify model repository ID
.WithHuggingFace(model: "meta-llama/Meta-Llama-3-8B-Instruct")
```

### "Model not found" (404)

**Problem:** Invalid or non-existent model repository ID.

**Solution:**
1. Check spelling of model ID
2. Visit [huggingface.co/models](https://huggingface.co/models) to find correct ID
3. Ensure model supports text generation

```csharp
// Wrong: Invalid model ID
.WithHuggingFace(model: "llama-3")

//  Correct: Full repository ID
.WithHuggingFace(model: "meta-llama/Meta-Llama-3-8B-Instruct")
```

### "Temperature must be between 0 and 100"

**Problem:** Invalid temperature value.

**Solution:** HuggingFace accepts 0-100 (unlike some providers that use 0-2):
```csharp
configure: opts => opts.Temperature = 0.7  //  Valid (0-100 range)
```

### "TopP must be between 0 and 1"

**Problem:** TopP value out of range.

**Solution:**
```csharp
configure: opts => opts.TopP = 0.9  //  Valid (0-1 range)
```

### "Model is loading" (503)

**Problem:** Model needs time to load into memory.

**Solution 1 - Wait automatically:**
```csharp
configure: opts =>
{
    opts.WaitForModel = true; // Wait for model to load
    opts.MaxTime = 60.0; // Allow up to 60 seconds
}
```

**Solution 2 - Retry manually:**
Wait a few seconds and retry (automatic retry is built-in).

### Rate limit exceeded (429)

**Problem:** Too many requests to HuggingFace API.

**Solution:**
- Provider automatically retries with exponential backoff
- Wait for rate limit to reset (usually ~1 hour)
- Upgrade to HuggingFace Pro for higher limits
- Consider caching responses

### Slow responses

**Problem:** Generation taking too long.

**Solutions:**
1. Use smaller models (`phi-2`, `TinyLlama`)
2. Reduce `MaxNewTokens`
3. Set `MaxTime` limit
4. Use more popular models (cached in memory)

```csharp
configure: opts =>
{
    opts.MaxNewTokens = 250; // Reduce output length
    opts.MaxTime = 30.0; // Set timeout
}
```

## ProviderOptionsJson Reference

When configuring via JSON file, all HuggingFace-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `HuggingFaceProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "huggingface",
        "modelName": "meta-llama/Meta-Llama-3-8B-Instruct",
        "apiKey": "hf_...",
        "providerOptionsJson": "{\"maxNewTokens\":250,\"temperature\":0.7,\"topP\":0.9,\"topK\":50,\"repetitionPenalty\":1.1,\"doSample\":true,\"useCache\":true}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `maxNewTokens` | int | ≥ 1 | New tokens to generate (not counting input, default: 250) |
| `temperature` | double | 0.0–100.0 | Sampling temperature (0=greedy, 1=normal, default: 1.0) |
| `topP` | double | 0.0–1.0 | Nucleus sampling threshold |
| `topK` | int | ≥ 1 | Top-K sampling |
| `repetitionPenalty` | double | ≥ 1.0 | Penalize repeated tokens (default: 1.0=no penalty) |
| `doSample` | bool | — | Use sampling (false=greedy decoding, default: true) |
| `numReturnSequences` | int | ≥ 1 | Number of sequences to return (default: 1) |
| `returnFullText` | bool | — | Include input prompt in response (default: true) |
| `maxTime` | double | ≥ 0.0 | Soft time limit in seconds |
| `useCache` | bool | — | Use inference API response cache (default: true) |
| `waitForModel` | bool | — | Wait for model to load instead of 503 (default: false) |

---

## HuggingFaceProviderConfig API Reference

### Core Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `MaxNewTokens` | `int?` | ≥ 1 | 250 | Maximum new tokens to generate |
| `Temperature` | `double?` | 0.0-100.0 | 1.0 | Sampling temperature (1.0=normal, 0=greedy, higher=random) |
| `TopP` | `double?` | 0.0-1.0 | - | Nucleus sampling threshold |
| `TopK` | `int?` | ≥ 0 | - | Top-K sampling limit |
| `RepetitionPenalty` | `double?` | ≥ 0 | 1.0 | Penalty for token repetition (higher=less repetition) |

### Generation Control

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DoSample` | `bool?` | `true` | Use sampling (false = greedy decoding) |
| `NumReturnSequences` | `int?` | 1 | Number of sequences to return |
| `ReturnFullText` | `bool?` | `true` | Include input prompt in output |

### Timing and Performance

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxTime` | `double?` | - | Maximum generation time in seconds (soft limit) |

### API Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseCache` | `bool?` | `true` | Use HuggingFace caching layer |
| `WaitForModel` | `bool?` | `false` | Wait for model to load (avoids 503 errors) |

### Advanced Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AdditionalProperties` | `Dictionary<string, object>?` | - | Model-specific custom parameters |

## Additional Resources

- [HuggingFace Documentation](https://huggingface.co/docs)
- [HuggingFace Models Hub](https://huggingface.co/models?pipeline_tag=text-generation)
- [Serverless Inference API](https://huggingface.co/docs/api-inference/index)
- [HuggingFace Pricing](https://huggingface.co/pricing)
- [Get API Token](https://huggingface.co/settings/tokens)
- [Model Cards & Licensing](https://huggingface.co/docs/hub/model-cards)

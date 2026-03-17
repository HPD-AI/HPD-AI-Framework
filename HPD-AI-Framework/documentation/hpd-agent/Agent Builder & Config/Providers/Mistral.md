# Mistral AI Provider

**Provider Key:** `mistral`

## Overview

The Mistral AI provider enables HPD-Agent to use Mistral's powerful language models, including open-source and commercial variants. Mistral AI offers high-performance models optimized for efficiency and multilingual capabilities.

**Key Features:**
-  Multiple model families (Mistral Large, Medium, Small, Mixtral)
-  Streaming support for real-time responses
-  Function/tool calling capabilities
- Vision support (not available)
-  JSON output mode
-  Deterministic generation with seed
-  Safety prompts and guardrails
-  Parallel tool calling
-  API key authentication

**For detailed API documentation, see:**
- [**MistralProviderConfig API Reference**](#mistralproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Mistral;

// Set API key via environment variable
Environment.SetEnvironmentVariable("MISTRAL_API_KEY", "your-api-key");

var agent = await new AgentBuilder()
    .WithMistral(model: "mistral-large-latest")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.Mistral
```

**Dependencies:**
- `Mistral.SDK` 2.3.0 - Community-maintained Mistral SDK
- `Microsoft.Extensions.AI` - AI abstractions

## Configuration

### Configuration Patterns

The Mistral provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.MaxTokens = 4096;
            opts.Temperature = 0.7m;
            opts.TopP = 0.9m;
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`mistral-config.json`:**
```json
{
    "name": "MistralAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "mistral",
        "modelName": "mistral-large-latest",
        "apiKey": "your-api-key",
        "providerOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7,\"safePrompt\":true}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("mistral-config.json");
```

The `providerOptionsJson` value is a JSON string containing Mistral-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "MistralAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "mistral",
        ModelName = "mistral-large-latest",
        ApiKey = "your-api-key"
    }
};

config.Provider.SetTypedProviderConfig(new MistralProviderConfig
{
    MaxTokens = 4096,
    Temperature = 0.7m,
    SafePrompt = true
});

var agent = await config.BuildAsync();
```

#### 4. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "MistralAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "mistral",
        ModelName = "mistral-large-latest",
        ApiKey = "your-api-key"
    }
};

var mistralOpts = new MistralProviderConfig
{
    MaxTokens = 4096,
    Temperature = 0.7m
};
config.Provider.SetTypedProviderConfig(mistralOpts);

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

The `MistralProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Parameters

```csharp
configure: opts =>
{
    // Maximum tokens to generate (model-specific limits)
    opts.MaxTokens = 4096;
}
```

#### Sampling Parameters

```csharp
configure: opts =>
{
    // Sampling temperature (0.0-1.0, default: 0.7)
    // Higher = more creative, Lower = more focused
    opts.Temperature = 0.7m;

    // Top-P nucleus sampling (0.0-1.0, default: 1.0)
    opts.TopP = 0.9m;
}
```

#### Determinism

```csharp
configure: opts =>
{
    // Seed for deterministic generation
    // Same seed + same input = same output
    opts.RandomSeed = 12345;
    opts.Temperature = 0m; // Use with seed for max determinism
}
```

#### Response Format

```csharp
configure: opts =>
{
    // Response format: "text" (default) or "json_object"
    opts.ResponseFormat = "json_object";

    // Note: Instruct the model to produce JSON in your prompt when using json_object
}
```

#### Safety

```csharp
configure: opts =>
{
    // Inject Mistral's safety guardrails
    opts.SafePrompt = true;
}
```

#### Tool/Function Calling

```csharp
configure: opts =>
{
    // Tool choice behavior: "auto" (default), "any", "none"
    opts.ToolChoice = "auto";

    // Enable parallel function calling
    opts.ParallelToolCalls = true;
}
```

#### Advanced Options

```csharp
configure: opts =>
{
    // Additional model-specific parameters
    opts.AdditionalProperties = new Dictionary<string, object>
    {
        ["custom_parameter"] = "value"
    };
}
```

## Authentication

Mistral AI uses API key authentication. The provider supports multiple authentication methods with priority ordering.

### Authentication Priority Order

1. **Explicit API key** in `WithMistral()` method
2. **Environment variable**: `MISTRAL_API_KEY`
3. **Configuration file**: `"mistral:ApiKey"` or `"Mistral:ApiKey"` in appsettings.json

### Method 1: Environment Variable (Recommended for Development)

```bash
export MISTRAL_API_KEY="your-api-key"
```

```csharp
// Automatically uses environment variable
var agent = await new AgentBuilder()
    .WithMistral(model: "mistral-large-latest")
    .BuildAsync();
```

### Method 2: Explicit API Key

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key")
    .BuildAsync();
```

**Security Warning:** Never hardcode API keys in source code. Use environment variables or secure configuration management instead.

### Method 3: Configuration File

**appsettings.json:**
```json
{
    "Mistral": {
        "ApiKey": "your-api-key",
        "ModelName": "mistral-large-latest"
    }
}
```

```csharp
var agent = await new AgentBuilder()
    .WithMistral(model: "mistral-large-latest")
    .BuildAsync();
```

### Getting Your API Key

1. Sign up at [Mistral AI Console](https://console.mistral.ai/)
2. Navigate to API Keys section
3. Create a new API key
4. Store securely using environment variables or secrets management

## Supported Models

Mistral AI provides access to multiple model families optimized for different use cases.

### Commercial Models

| Model ID | Context | Best For |
|----------|---------|----------|
| `mistral-large-latest` | 128k | Complex reasoning, coding, analysis |
| `mistral-medium-latest` | 32k | Balanced performance and cost |
| `mistral-small-latest` | 32k | Fast, cost-effective for simple tasks |

### Open-Source Models

| Model ID | Context | Best For |
|----------|---------|----------|
| `open-mistral-7b` | 32k | Open-source, general purpose |
| `open-mixtral-8x7b` | 32k | Mixture of Experts, high performance |

### Embeddings

| Model ID | Dimensions | Best For |
|----------|------------|----------|
| `mistral-embed` | 1024 | Text embeddings, semantic search |

**For the latest models, see:**
- [Mistral AI Models Documentation](https://docs.mistral.ai/getting-started/models/)

## Advanced Features

### JSON Output Mode

Force the model to return valid JSON:

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.ResponseFormat = "json_object";
            opts.Temperature = 0.3m; // Lower for structured output
        })
    .BuildAsync();

var response = await agent.RunAsync(
    "Return user data as JSON: Name: John Doe, Age: 30, City: Paris");
```

**Note:** Always instruct the model to produce JSON in your prompt when using `json_object` mode.

### Deterministic Generation

Produce identical outputs for the same inputs:

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-small-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.RandomSeed = 12345; // Same seed = same output
            opts.Temperature = 0m; // No randomness
        })
    .BuildAsync();

// Multiple calls with same input will produce identical outputs
var response1 = await agent.RunAsync("Generate a story.");
var response2 = await agent.RunAsync("Generate a story.");
// response1 == response2
```

### Safety Prompts

Inject Mistral's safety guardrails:

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.SafePrompt = true; // Add safety guardrails
        })
    .BuildAsync();
```

**Benefits:**
- Prevents harmful outputs
- Reduces inappropriate responses
- Enforces ethical guidelines

### Parallel Tool Calling

Enable the model to call multiple tools simultaneously:

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.ToolChoice = "auto";
            opts.ParallelToolCalls = true; // Call multiple tools at once
        })
    .WithToolkit<WeatherToolkit>()
    .WithToolkit<CalculatorToolkit>()
    .BuildAsync();
```

### Client Middleware

Add logging, caching, or custom processing:

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        clientFactory: client =>
            new LoggingChatClient(
                new CachingChatClient(client)))
    .BuildAsync();
```

## Error Handling

The Mistral provider includes intelligent error classification and automatic retry logic.

### Error Categories

| Category | HTTP Status | Retry Behavior | Examples |
|----------|-------------|----------------|----------|
| **AuthError** | 401, 403 | No retry | Invalid API key, insufficient permissions |
| **RateLimitRetryable** | 429 |  Exponential backoff | Rate limit exceeded, quota exceeded |
| **ClientError** | 400, 404 | No retry | Invalid parameters, model not found |
| **Transient** | 503 |  Retry | Service unavailable, timeout |
| **ServerError** | 500-599 |  Retry | Internal server error |

### Automatic Retry Logic

The provider automatically retries transient errors with exponential backoff:

```
Attempt 0: 1 second delay
Attempt 1: 2 seconds delay
Attempt 2: 4 seconds delay
Attempt 3: 8 seconds delay
```

### Common Exceptions

#### 401 Unauthorized
```
Mistral rejected your authorization, invalid API key
```
**Solution:** Verify API key is correct and active

#### 429 Rate Limit Exceeded
```
Rate limit exceeded
```
**Solution:** Automatically retried with backoff. For persistent issues, upgrade your plan

#### 400 Bad Request
```
Invalid request parameters
```
**Solution:** Check temperature range (0.0-1.0), responseFormat values, etc.

#### 503 Service Unavailable
```
Service temporarily unavailable
```
**Solution:** Automatically retried. If persistent, check Mistral AI status

#### 404 Model Not Found
```
Model not found
```
**Solution:** Verify model ID and ensure you have access

## Examples

### Example 1: Basic Chat

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Mistral;

var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key")
    .BuildAsync();

var response = await agent.RunAsync("Explain quantum computing in simple terms.");
Console.WriteLine(response);
```

### Example 2: Function Calling with Tools

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
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts => opts.ToolChoice = "auto")
    .WithToolkit<WeatherToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What's the weather in Seattle?");
```

### Example 3: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key")
    .BuildAsync();

await foreach (var chunk in agent.RunAsync("Write a short story about AI."))
{
    Console.Write(chunk);
}
```

### Example 4: JSON Output Mode

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.ResponseFormat = "json_object";
            opts.Temperature = 0.3m;
        })
    .BuildAsync();

var response = await agent.RunAsync(@"
    Extract the following person data as JSON:
    Name: Alice Smith
    Age: 28
    City: New York
    Occupation: Engineer
");
Console.WriteLine(response);
// Output: {"name":"Alice Smith","age":28,"city":"New York","occupation":"Engineer"}
```

### Example 5: Deterministic Generation for Testing

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-small-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.RandomSeed = 42;
            opts.Temperature = 0m;
        })
    .BuildAsync();

// Use in tests to ensure consistent behavior
var response1 = await agent.RunAsync("Generate a test case");
var response2 = await agent.RunAsync("Generate a test case");
Assert.Equal(response1, response2); // Will pass
```

### Example 6: Creative Writing with High Temperature

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.Temperature = 1.0m; // Maximum creativity
            opts.TopP = 0.95m;
            opts.MaxTokens = 2048;
        })
    .BuildAsync();

var response = await agent.RunAsync("Write a creative poem about the stars.");
```

### Example 7: Precise Code Generation

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.Temperature = 0m; // No randomness
            opts.MaxTokens = 4096;
        })
    .BuildAsync();

var response = await agent.RunAsync("Write a Python function to sort a list.");
```

### Example 8: Safe Content Generation

```csharp
var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.SafePrompt = true; // Enable safety guardrails
            opts.Temperature = 0.7m;
        })
    .BuildAsync();

var response = await agent.RunAsync("Generate a story for children.");
```

### Example 9: Parallel Tool Execution

```csharp
public class MathToolkit
{
    [Function("Add two numbers")]
    public int Add(int a, int b) => a + b;

    [Function("Multiply two numbers")]
    public int Multiply(int a, int b) => a * b;
}

var agent = await new AgentBuilder()
    .WithMistral(
        model: "mistral-large-latest",
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.ParallelToolCalls = true; // Execute tools in parallel
        })
    .WithToolkit<MathToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What is 5+3 and 4*7?");
// Model can call both Add and Multiply simultaneously
```

### Example 10: Multi-Model Strategy

```csharp
// Use different models for different tasks
var config = new AgentConfig
{
    Provider = new ProviderConfig
    {
        ProviderKey = "mistral",
        ApiKey = "your-api-key"
    }
};

// Fast model for simple tasks
var simpleAgent = new AgentBuilder(config)
    .WithMistral(model: "mistral-small-latest")
    .BuildAsync();

// Powerful model for complex tasks
var complexAgent = new AgentBuilder(config)
    .WithMistral(
        model: "mistral-large-latest",
        configure: opts => opts.MaxTokens = 8192)
    .BuildAsync();

// Route based on task complexity
var response = taskIsSimple
    ? await simpleAgent.RunAsync(prompt)
    : await complexAgent.RunAsync(prompt);
```

## Troubleshooting

### "API key is required for Mistral"

**Problem:** Missing API key configuration.

**Solution:**
```csharp
// Option 1: Environment variable
Environment.SetEnvironmentVariable("MISTRAL_API_KEY", "your-api-key");

// Option 2: Explicit parameter
.WithMistral(model: "...", apiKey: "your-api-key")

// Option 3: Config file
// Add to appsettings.json: "Mistral": {"ApiKey": "your-api-key"}
```

### "401 Unauthorized"

**Problem:** Invalid or expired API key.

**Solution:**
1. Verify API key is correct
2. Check key is active in Mistral AI Console
3. Ensure no extra spaces or characters
4. Generate new API key if necessary

### "Temperature must be between 0.0 and 1.0"

**Problem:** Invalid temperature value.

**Solution:**
```csharp
configure: opts => opts.Temperature = 0.7m  //  Valid (0.0-1.0)
// NOT: opts.Temperature = 1.5m  // Invalid for Mistral
```

### "ResponseFormat must be one of: text, json_object"

**Problem:** Invalid response format.

**Solution:**
```csharp
configure: opts => opts.ResponseFormat = "json_object"  //  Valid
// NOT: opts.ResponseFormat = "json_schema"  // Not supported by Mistral
```

### "ToolChoice must be one of: auto, any, none"

**Problem:** Invalid tool choice value.

**Solution:**
```csharp
configure: opts => opts.ToolChoice = "auto"  //  Valid
// NOT: opts.ToolChoice = "required"  // Use "any" instead
```

### Rate Limiting (429)

**Problem:** Too many requests.

**Solution:**
- Provider automatically retries with exponential backoff
- For persistent issues:
  1. Upgrade your Mistral AI plan
  2. Implement request throttling
  3. Reduce request frequency

### Connection Timeout

**Problem:** Requests timing out.

**Solution:**
```csharp
configure: opts =>
{
    opts.MaxTokens = 2048; // Reduce output size
}
// Or increase HttpClient timeout in your app
```

### Model Not Found (404)

**Problem:** Invalid model ID or no access.

**Solution:**
1. Verify model ID matches exactly (case-sensitive)
2. Check [available models](https://docs.mistral.ai/getting-started/models/)
3. Ensure you have access to the model

## ProviderOptionsJson Reference

When configuring via JSON file, all Mistral-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `MistralProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "mistral",
        "modelName": "mistral-large-latest",
        "apiKey": "your-api-key",
        "providerOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7,\"topP\":1.0,\"randomSeed\":42,\"safePrompt\":true,\"toolChoice\":\"auto\",\"parallelToolCalls\":true}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `maxTokens` | int | ≥ 1 | Max tokens to generate |
| `temperature` | decimal | 0.0–1.0 | Sampling randomness (default: 0.7) |
| `topP` | decimal | 0.0–1.0 | Nucleus sampling threshold (default: 1.0) |
| `randomSeed` | int | any | Seed for deterministic generation |
| `responseFormat` | string | `"text"`, `"json_object"` | Output format |
| `safePrompt` | bool | — | Inject Mistral safety guardrails (default: false) |
| `toolChoice` | string | `"auto"`, `"any"`, `"none"` | Tool selection behavior |
| `parallelToolCalls` | bool | — | Enable parallel tool execution (default: true) |

---

## MistralProviderConfig API Reference

### Core Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `MaxTokens` | `int?` | ≥ 1 | Model-specific | Maximum tokens to generate |

### Sampling Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `Temperature` | `decimal?` | 0.0-1.0 | 0.7 | Sampling temperature (creativity) |
| `TopP` | `decimal?` | 0.0-1.0 | 1.0 | Nucleus sampling threshold |

### Determinism

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RandomSeed` | `int?` | - | Seed for deterministic generation |

### Response Format

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ResponseFormat` | `string?` | "text", "json_object" | "text" | Output format |

### Safety

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SafePrompt` | `bool?` | `false` | Inject Mistral's safety guardrails |

### Tool/Function Calling

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ToolChoice` | `string?` | "auto", "any", "none" | "auto" | Tool selection behavior |
| `ParallelToolCalls` | `bool?` | `true` | Enable parallel function calling |

### Advanced Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AdditionalProperties` | `Dictionary<string, object>?` | - | Custom model parameters |

## Additional Resources

- [Mistral AI Documentation](https://docs.mistral.ai/)
- [Mistral AI Models](https://docs.mistral.ai/getting-started/models/)
- [Mistral AI Pricing](https://mistral.ai/pricing/)
- [Mistral AI Console](https://console.mistral.ai/)
- [Mistral.SDK on GitHub](https://github.com/tghamm/Mistral.SDK)
- [API Reference](https://docs.mistral.ai/api/)

---

**Note:** This provider uses the community-maintained [Mistral.SDK](https://github.com/tghamm/Mistral.SDK), which is not an official Mistral AI SDK but provides comprehensive integration with Microsoft.Extensions.AI.

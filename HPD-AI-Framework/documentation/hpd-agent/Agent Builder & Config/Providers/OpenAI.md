# OpenAI Provider

**Provider Key:** `openai`

## Overview

The OpenAI provider enables HPD-Agent to use OpenAI's powerful language models, including GPT-4, GPT-3.5, and specialized models like o1 (reasoning) and gpt-4o-audio-preview (audio). The provider uses the official OpenAI .NET SDK to deliver cutting-edge AI capabilities with comprehensive configuration options.

**Key Features:**
-  Latest GPT models (GPT-4o, GPT-4 Turbo, GPT-3.5 Turbo)
-  Streaming support for real-time responses
-  Function/tool calling capabilities
-  Vision support (image understanding)
-  Audio input/output (gpt-4o-audio-preview)
-  Reasoning models (o1 series)
-  Structured JSON outputs with schema validation
-  Prompt caching for reduced latency
-  Web search integration (experimental)
-  Native AOT compatibility

**For detailed API documentation, see:**
- [**OpenAIProviderConfig API Reference**](#openaiproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Providers.OpenAI;

// Set API key via environment variable (recommended)
Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-...");

var agent = await new AgentBuilder()
    .WithOpenAI(model: "gpt-4o")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.OpenAI
```

**Dependencies:**
- `OpenAI` (>= 2.0) - Official OpenAI .NET SDK
- `Azure.AI.OpenAI` (>= 2.1.0) - Azure OpenAI support
- `Microsoft.Extensions.AI.OpenAI` - Microsoft.Extensions.AI integration
- `Microsoft.Extensions.AI` - AI abstractions

## Configuration

### Configuration Patterns

The OpenAI provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts =>
        {
            opts.MaxOutputTokenCount = 4096;
            opts.Temperature = 0.7f;
            opts.TopP = 0.95f;
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`agent.json`:**
```json
{
    "name": "OpenAIAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "openai",
        "modelName": "gpt-4o",
        "apiKey": "sk-...",
        "providerOptionsJson": "{\"temperature\":0.7,\"maxOutputTokenCount\":4096,\"topP\":0.95}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("agent.json");
```

The `providerOptionsJson` value is a JSON string containing OpenAI-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "OpenAIAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "openai",
        ModelName = "gpt-4o",
        ApiKey = "sk-..."
    }
};

config.Provider.SetTypedProviderConfig(new OpenAIProviderConfig
{
    MaxOutputTokenCount = 4096,
    Temperature = 0.7f,
    TopP = 0.95f
});

var agent = await config.BuildAsync();
```

#### 3. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "OpenAIAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "openai",
        ModelName = "gpt-4o",
        ApiKey = "sk-..."
    }
};

var openAIOpts = new OpenAIProviderConfig
{
    MaxOutputTokenCount = 4096,
    Temperature = 0.7f
};
config.Provider.SetTypedProviderConfig(openAIOpts);

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

The `OpenAIProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Parameters

```csharp
configure: opts =>
{
    // Maximum tokens to generate (model-specific limits)
    opts.MaxOutputTokenCount = 4096;

    // Sampling temperature (0.0-2.0, default: model-specific)
    opts.Temperature = 0.7f;

    // Top-P nucleus sampling (0.0-1.0)
    opts.TopP = 0.95f;

    // Frequency penalty (-2.0 to 2.0)
    opts.FrequencyPenalty = 0.5f;

    // Presence penalty (-2.0 to 2.0)
    opts.PresencePenalty = 0.5f;

    // Stop sequences (max 4)
    opts.StopSequences = new List<string> { "\n\n", "END" };
}
```

#### Response Format

```csharp
configure: opts =>
{
    // Response format: "text", "json_object", "json_schema"
    opts.ResponseFormat = "json_schema";

    // Schema name (required for json_schema)
    opts.JsonSchemaName = "UserInfo";

    // JSON schema definition
    opts.JsonSchema = @"{
        ""type"": ""object"",
        ""properties"": {
            ""name"": { ""type"": ""string"" },
            ""age"": { ""type"": ""number"" }
        },
        ""required"": [""name""]
    }";

    // Schema description (optional)
    opts.JsonSchemaDescription = "User information schema";

    // Enforce strict schema adherence (default: true)
    opts.JsonSchemaIsStrict = true;
}
```

#### Tool/Function Calling

```csharp
configure: opts =>
{
    // Tool choice behavior: "auto", "none", "required"
    opts.ToolChoice = "auto";

    // Enable parallel tool execution (default: true)
    opts.AllowParallelToolCalls = true;
}
```

#### Reasoning Models (o1 Series)

```csharp
configure: opts =>
{
    // Reasoning effort: "low", "medium", "high", "minimal"
    opts.ReasoningEffortLevel = "high";

    // Higher max tokens recommended for reasoning
    opts.MaxOutputTokenCount = 8192;
}
```

#### Audio (gpt-4o-audio-preview)

```csharp
configure: opts =>
{
    // Response modalities: "text", "audio", "text,audio"
    opts.ResponseModalities = "text,audio";

    // Voice selection
    opts.AudioVoice = "alloy"; // alloy, ash, ballad, coral, echo, sage, shimmer, verse

    // Audio format
    opts.AudioFormat = "mp3"; // wav, mp3, flac, opus, pcm16
}
```

#### Log Probabilities

```csharp
configure: opts =>
{
    // Return log probabilities for tokens
    opts.IncludeLogProbabilities = true;

    // Number of top tokens (0-20)
    opts.TopLogProbabilityCount = 5;

    // Token bias mapping (token ID → bias value)
    opts.LogitBiases = new Dictionary<int, int>
    {
        [1234] = -100, // Ban token
        [5678] = 100   // Strongly prefer token
    };
}
```

#### Advanced Options

```csharp
configure: opts =>
{
    // Deterministic generation seed
    opts.Seed = 12345;

    // Service tier: "auto", "default"
    opts.ServiceTier = "default";

    // End-user identifier for abuse monitoring
    opts.EndUserId = "user-123";

    // Safety identifier for policy violation detection
    opts.SafetyIdentifier = "session-abc";

    // Store outputs for distillation/evals
    opts.StoredOutputEnabled = true;

    // Dashboard filtering tags
    opts.Metadata = new Dictionary<string, string>
    {
        ["environment"] = "production",
        ["version"] = "1.0"
    };

    // Enable web search (experimental)
    opts.WebSearchEnabled = true;

    // Additional custom parameters
    opts.AdditionalProperties = new Dictionary<string, object>
    {
        ["customField"] = "value"
    };
}
```

## Authentication

OpenAI uses API keys for authentication. The provider supports multiple authentication methods with priority ordering.

### Authentication Priority Order

1. **Explicit API key** in method parameter
2. **Environment variable**: `OPENAI_API_KEY`
3. **Configuration file**: `appsettings.json` → `"OpenAI:ApiKey"`

### Method 1: Environment Variables (Recommended)

```bash
export OPENAI_API_KEY="sk-..."
```

```csharp
// Automatically uses environment variable
var agent = await new AgentBuilder()
    .WithOpenAI(model: "gpt-4o")
    .BuildAsync();
```

### Method 2: Configuration Files

**appsettings.json:**
```json
{
    "OpenAI": {
        "ApiKey": "sk-..."
    }
}
```

```csharp
// Automatically resolved from config
var agent = await new AgentBuilder()
    .WithOpenAI(model: "gpt-4o")
    .BuildAsync();
```

### Method 3: Explicit Parameter

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...")
    .BuildAsync();
```

**Security Warning:** Never hardcode API keys in source code. Use environment variables or secure configuration management instead.

### Getting an API Key

1. Sign up at [OpenAI Platform](https://platform.openai.com/)
2. Navigate to [API Keys](https://platform.openai.com/api-keys)
3. Create a new secret key
4. Store securely and never commit to version control

## Azure OpenAI Support

The provider also supports traditional Azure OpenAI endpoints (API key-based).

**Note:** For modern Azure AI Projects/Foundry with OAuth support, use the `HPD-Agent.Providers.AzureAI` package instead.

```csharp
var agent = await new AgentBuilder()
    .WithAzureOpenAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4", // deployment name
        apiKey: "your-api-key",
        configure: opts =>
        {
            opts.MaxOutputTokenCount = 4096;
            opts.Temperature = 0.7f;
        })
    .BuildAsync();
```

### Azure Authentication

**Environment Variables:**
```bash
export AZURE_OPENAI_ENDPOINT="https://my-resource.openai.azure.com"
export AZURE_OPENAI_API_KEY="your-api-key"
```

**Configuration File:**
```json
{
    "AzureOpenAI": {
        "Endpoint": "https://my-resource.openai.azure.com",
        "ApiKey": "your-api-key"
    }
}
```

## Supported Models

### GPT-4 Series (Latest)

- **gpt-4o** - Latest flagship model with vision, multimodal capabilities
- **gpt-4o-mini** - Fast, affordable model for simple tasks
- **gpt-4-turbo** - Previous generation with 128K context
- **gpt-4** - Original GPT-4 model

### GPT-3.5 Series

- **gpt-3.5-turbo** - Fast, cost-effective for most tasks
- **gpt-3.5-turbo-16k** - Extended 16K context window

### Specialized Models

- **o1-preview** - Advanced reasoning model
- **o1-mini** - Fast reasoning model
- **gpt-4o-audio-preview** - Audio input/output capabilities

### Fine-Tuned Models

Custom models based on GPT-4 or GPT-3.5 are supported. Use your custom model ID.

**For the complete and up-to-date model list, see:**
- [OpenAI Models Documentation](https://platform.openai.com/docs/models)

## Advanced Features

### Structured JSON Outputs

Generate validated JSON responses that conform to your schema.

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts =>
        {
            opts.ResponseFormat = "json_schema";
            opts.JsonSchemaName = "UserProfile";
            opts.JsonSchema = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"" },
                    ""age"": { ""type"": ""number"" },
                    ""email"": { ""type"": ""string"", ""format"": ""email"" },
                    ""interests"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" }
                    }
                },
                ""required"": [""name"", ""age"", ""email""],
                ""additionalProperties"": false
            }";
            opts.JsonSchemaIsStrict = true;
        })
    .BuildAsync();

var response = await agent.RunAsync("Extract user info: John Doe, 30 years old, john@example.com, likes coding and hiking");
// Guaranteed to match schema
```

**Benefits:**
-  Guaranteed schema compliance
-  No parsing errors
-  Type-safe outputs
-  Reduced hallucination

### Reasoning Models (o1 Series)

Advanced reasoning capabilities for complex problem-solving.

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "o1-preview",
        apiKey: "sk-...",
        configure: opts =>
        {
            // Higher reasoning effort for complex problems
            opts.ReasoningEffortLevel = "high";

            // Reasoning models need more output tokens
            opts.MaxOutputTokenCount = 16384;
        })
    .BuildAsync();

var response = await agent.RunAsync(@"
    Solve this problem step by step:
    A train travels from city A to B at 60 mph and returns at 40 mph.
    What is the average speed for the round trip?
");
```

**Reasoning Effort Levels:**
- **"minimal"** - Fastest, basic reasoning
- **"low"** - Quick reasoning for simple problems
- **"medium"** - Balanced (default)
- **"high"** - Deep reasoning for complex problems

### Audio Input/Output

Process and generate audio with gpt-4o-audio-preview.

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o-audio-preview",
        apiKey: "sk-...",
        configure: opts =>
        {
            // Enable both text and audio outputs
            opts.ResponseModalities = "text,audio";

            // Select voice
            opts.AudioVoice = "alloy"; // Natural, balanced voice

            // Choose format
            opts.AudioFormat = "mp3"; // Or wav, flac, opus, pcm16
        })
    .BuildAsync();
```

**Available Voices:**
- **alloy** - Neutral, balanced (recommended)
- **ash** - Calm, professional
- **ballad** - Warm, expressive
- **coral** - Friendly, energetic
- **echo** - Clear, authoritative
- **sage** - Wise, mature
- **shimmer** - Bright, enthusiastic
- **verse** - Rich, narrative

### Deterministic Generation

Generate consistent outputs for the same input.

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts =>
        {
            // Set seed for determinism
            opts.Seed = 12345;

            // Use zero temperature for maximum consistency
            opts.Temperature = 0.0f;
        })
    .BuildAsync();

// Same input + seed = same output
var response1 = await agent.RunAsync("Generate a random number");
var response2 = await agent.RunAsync("Generate a random number");
// response1 ≈ response2 (best-effort determinism)
```

**Note:** Determinism is best-effort and may vary across model versions or infrastructure.

### Token Bias (Logit Manipulation)

Control token probabilities to guide generation.

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts =>
        {
            opts.LogitBiases = new Dictionary<int, int>
            {
                [1234] = -100,  // Ban token (e.g., profanity)
                [5678] = 50,    // Strongly prefer token
                [9012] = 10     // Slightly prefer token
            };
        })
    .BuildAsync();
```

**Bias Values:**
- **-100 to -1** - Reduce likelihood (ban at -100)
- **0** - Neutral (default)
- **1 to 100** - Increase likelihood (force at 100)

**Use Cases:**
- Content filtering
- Domain-specific vocabulary preference
- Format enforcement

### Client Middleware

Add custom middleware for logging, caching, or monitoring.

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts => opts.MaxOutputTokenCount = 4096,
        clientFactory: client =>
        {
            // Wrap with custom middleware
            var cached = new CachingChatClient(client, cache);
            var logged = new LoggingChatClient(cached, logger);
            return logged;
        })
    .BuildAsync();
```

## Error Handling

The OpenAI provider includes intelligent error classification and automatic retry logic.

### Error Categories

| Category | HTTP Status | Retry Behavior | Examples |
|----------|-------------|----------------|----------|
| **AuthError** | 401, 403 | No retry | Invalid API key, insufficient permissions |
| **RateLimitRetryable** | 429 |  Exponential backoff | Rate limit exceeded (temporary) |
| **RateLimitTerminal** | 429 | No retry | Quota exhausted, account suspended |
| **ContextWindow** | 400 | No retry | Context length exceeded |
| **ClientError** | 400, 404 | No retry | Invalid request, model not found |
| **Transient** | 408, 503, 504 |  Retry | Timeout, service unavailable |
| **ServerError** | 500-599 |  Retry | Internal server error |

### Automatic Retry Configuration

Retryable errors use exponential backoff with jitter automatically. No configuration needed, but you can control behavior through the agent's error handling middleware.

### Common Exceptions

#### 401 Unauthorized
```
Invalid authentication credentials
```
**Solution:** Verify API key is correct and not revoked

#### 429 Rate Limit Exceeded
```
Rate limit reached for requests
```
**Solution:**
- Automatically retried with exponential backoff
- Upgrade tier or request quota increase
- Reduce request frequency

#### 429 Quota Exceeded (Terminal)
```
You exceeded your current quota
```
**Solution:**
- Not retried (quota exhausted)
- Add credits or upgrade plan
- Check billing settings

#### 400 Context Length Exceeded
```
This model's maximum context length is X tokens
```
**Solution:**
- Reduce input length
- Decrease `MaxOutputTokenCount`
- Use model with larger context (gpt-4-turbo: 128K)

#### 404 Model Not Found
```
The model 'model-name' does not exist
```
**Solution:** Verify model name spelling and availability

#### 400 Validation Error
```
Invalid request parameters
```
**Solution:** Check parameter ranges and required fields

## Examples

### Example 1: Basic Chat

```csharp
using HPD.Agent;
using HPD.Agent.Providers.OpenAI;

var agent = await new AgentBuilder()
    .WithOpenAI(model: "gpt-4o", apiKey: "sk-...")
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
        // In production, call actual weather API
        return $"The weather in {location} is sunny, 72°F";
    }

    [Function("Get weather forecast for next N days")]
    public string GetForecast(string location, int days)
    {
        return $"{days}-day forecast for {location}: Mostly sunny";
    }
}

var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts => opts.ToolChoice = "auto")
    .WithToolkit<WeatherToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What's the weather in Seattle? Also get the 5-day forecast.");
Console.WriteLine(response);
```

### Example 3: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(model: "gpt-4o", apiKey: "sk-...")
    .BuildAsync();

Console.Write("AI: ");
await foreach (var chunk in agent.RunAsync("Write a short story about AI."))
{
    Console.Write(chunk);
}
Console.WriteLine();
```

### Example 4: Structured Data Extraction

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts =>
        {
            opts.ResponseFormat = "json_schema";
            opts.JsonSchemaName = "ProductReview";
            opts.JsonSchema = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""sentiment"": { ""type"": ""string"", ""enum"": [""positive"", ""negative"", ""neutral""] },
                    ""rating"": { ""type"": ""number"", ""minimum"": 1, ""maximum"": 5 },
                    ""summary"": { ""type"": ""string"" },
                    ""pros"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
                    ""cons"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
                },
                ""required"": [""sentiment"", ""rating"", ""summary""]
            }";
        })
    .BuildAsync();

var review = @"
This product is amazing! The build quality is excellent and it works perfectly.
The only downside is the price, but you get what you pay for. Highly recommended!
";

var response = await agent.RunAsync($"Extract review data:\n{review}");
var data = JsonSerializer.Deserialize<ProductReview>(response);
Console.WriteLine($"Sentiment: {data.Sentiment}, Rating: {data.Rating}/5");
```

### Example 5: Complex Reasoning Task

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "o1-preview",
        apiKey: "sk-...",
        configure: opts =>
        {
            opts.ReasoningEffortLevel = "high";
            opts.MaxOutputTokenCount = 16384;
        })
    .BuildAsync();

var problem = @"
You have 3 boxes. Box A contains 2 red balls and 1 blue ball.
Box B contains 1 red ball and 2 blue balls. Box C contains 3 red balls.
You randomly select a box and then randomly draw a ball from it.
If you drew a red ball, what is the probability it came from Box C?
";

var response = await agent.RunAsync(problem);
Console.WriteLine(response);
```

### Example 6: Multi-Model Strategy

```csharp
// Use expensive model for complex tasks
var expertAgent = await new AgentBuilder()
    .WithOpenAI(model: "gpt-4o", apiKey: "sk-...")
    .BuildAsync();

// Use cheaper model for simple tasks
var economyAgent = await new AgentBuilder()
    .WithOpenAI(model: "gpt-4o-mini", apiKey: "sk-...")
    .BuildAsync();

async Task<string> ProcessQuery(string query)
{
    // Route based on complexity
    if (query.Length > 500 || query.Contains("analyze") || query.Contains("complex"))
    {
        return await expertAgent.RunAsync(query);
    }
    return await economyAgent.RunAsync(query);
}
```

### Example 7: Creative Writing with Custom Sampling

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        apiKey: "sk-...",
        configure: opts =>
        {
            // High temperature for creativity
            opts.Temperature = 1.2f;

            // Diverse token selection
            opts.TopP = 0.95f;

            // Reduce repetition
            opts.FrequencyPenalty = 0.7f;
            opts.PresencePenalty = 0.6f;

            // Custom stop sequences
            opts.StopSequences = new List<string> { "THE END", "\n---\n" };
        })
    .BuildAsync();

var response = await agent.RunAsync("Write a creative short story about time travel.");
```

## Troubleshooting

### "API key is required for OpenAI"

**Problem:** Missing API key configuration.

**Solution:**
```csharp
// Option 1: Explicit parameter
.WithOpenAI(model: "gpt-4o", apiKey: "sk-...")

// Option 2: Environment variable
Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-...");

// Option 3: appsettings.json
{
    "OpenAI": {
        "ApiKey": "sk-..."
    }
}
```

### "Model is required"

**Problem:** Missing model name.

**Solution:** Provide model name in `WithOpenAI()` call:
```csharp
.WithOpenAI(model: "gpt-4o", apiKey: "sk-...")
```

### 401 Unauthorized

**Problem:** Invalid or expired API key.

**Solution:**
1. Verify API key format (starts with "sk-")
2. Check key is not revoked at [OpenAI Platform](https://platform.openai.com/api-keys)
3. Ensure key has required permissions

### 429 Too Many Requests

**Problem:** Rate limit exceeded.

**Solution:**
- Provider automatically retries with exponential backoff
- For persistent issues:
  - Upgrade to higher tier
  - Request quota increase
  - Reduce request frequency
  - Implement client-side rate limiting

### 429 Quota Exceeded (Terminal)

**Problem:** Usage quota exhausted.

**Solution:**
- Add credits to account
- Upgrade billing plan
- Check usage at [OpenAI Usage Dashboard](https://platform.openai.com/usage)

### 400 Context Length Exceeded

**Problem:** Input + output exceeds model's context window.

**Solution:**
```csharp
configure: opts =>
{
    // Reduce output tokens
    opts.MaxOutputTokenCount = 2048;
}

// Or use model with larger context
.WithOpenAI(model: "gpt-4-turbo") // 128K context
```

### "Temperature must be between 0 and 2"

**Problem:** Invalid temperature value.

**Solution:**
```csharp
configure: opts => opts.Temperature = 0.7f  //  Valid (0.0-2.0)
// NOT: opts.Temperature = 3.0f  // Invalid
```

### "JsonSchemaName is required when ResponseFormat is json_schema"

**Problem:** Missing required schema properties.

**Solution:**
```csharp
configure: opts =>
{
    opts.ResponseFormat = "json_schema";
    opts.JsonSchemaName = "MySchema";  //  Required
    opts.JsonSchema = "{...}";         //  Required
}
```

### Slow responses

**Problem:** High latency.

**Solution:**
```csharp
configure: opts =>
{
    // Use faster model
    // .WithOpenAI(model: "gpt-4o-mini") or "gpt-3.5-turbo"

    // Reduce output tokens
    opts.MaxOutputTokenCount = 1024;

    // Lower temperature (faster sampling)
    opts.Temperature = 0.2f;
}
```

## ProviderOptionsJson Reference

When configuring via JSON file, all OpenAI-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `OpenAIProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "openai",
        "modelName": "gpt-4o",
        "apiKey": "sk-...",
        "providerOptionsJson": "{\"temperature\":0.7,\"maxOutputTokenCount\":4096,\"topP\":0.95,\"frequencyPenalty\":0.5,\"presencePenalty\":0.5,\"seed\":42,\"toolChoice\":\"auto\",\"allowParallelToolCalls\":true,\"serviceTier\":\"auto\",\"endUserId\":\"user-123\"}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `maxOutputTokenCount` | int | ≥ 1 | Max tokens to generate |
| `temperature` | float | 0.0–2.0 | Sampling randomness |
| `topP` | float | 0.0–1.0 | Nucleus sampling threshold |
| `frequencyPenalty` | float | -2.0–2.0 | Reduce token repetition |
| `presencePenalty` | float | -2.0–2.0 | Encourage topic diversity |
| `stopSequences` | string[] | max 4 | Stop generation sequences |
| `seed` | long | any | Deterministic generation seed |
| `responseFormat` | string | `"text"`, `"json_object"`, `"json_schema"` | Output format |
| `jsonSchemaName` | string | — | Schema name (required for `json_schema`) |
| `jsonSchema` | string | — | JSON schema definition string |
| `jsonSchemaDescription` | string | — | Optional schema description |
| `jsonSchemaIsStrict` | bool | — | Enforce strict schema (default `true`) |
| `toolChoice` | string | `"auto"`, `"none"`, `"required"` | Tool selection behavior |
| `allowParallelToolCalls` | bool | — | Enable parallel tool execution |
| `includeLogProbabilities` | bool | — | Return token log probabilities |
| `topLogProbabilityCount` | int | 0–20 | Number of top tokens to return |
| `logitBiases` | object | token ID → -100–100 | Token probability biases |
| `reasoningEffortLevel` | string | `"low"`, `"medium"`, `"high"`, `"minimal"` | o1 reasoning effort |
| `responseModalities` | string | `"text"`, `"audio"`, `"text,audio"` | Output modalities |
| `audioVoice` | string | `"alloy"`, `"ash"`, `"ballad"`, `"coral"`, `"echo"`, `"sage"`, `"shimmer"`, `"verse"` | Audio voice |
| `audioFormat` | string | `"wav"`, `"mp3"`, `"flac"`, `"opus"`, `"pcm16"` | Audio format |
| `serviceTier` | string | `"auto"`, `"default"` | Service tier |
| `endUserId` | string | — | End-user identifier for abuse monitoring |
| `safetyIdentifier` | string | — | Safety/policy identifier |
| `storedOutputEnabled` | bool | — | Store outputs for distillation/evals |
| `metadata` | object | key-value strings | Dashboard filtering tags |
| `webSearchEnabled` | bool | — | Enable web search (experimental) |

---

## OpenAIProviderConfig API Reference

### Core Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `MaxOutputTokenCount` | `int?` | ≥ 1 | Model-specific | Maximum tokens to generate |
| `Temperature` | `float?` | 0.0-2.0 | Model-specific | Sampling temperature |
| `TopP` | `float?` | 0.0-1.0 | - | Nucleus sampling threshold |
| `FrequencyPenalty` | `float?` | -2.0 to 2.0 | - | Reduce token repetition |
| `PresencePenalty` | `float?` | -2.0 to 2.0 | - | Encourage topic diversity |
| `StopSequences` | `List<string>?` | Max 4 | - | Stop generation sequences |

### Determinism

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Seed` | `long?` | - | Deterministic generation seed |

### Response Format

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ResponseFormat` | `string?` | "text", "json_object", "json_schema" | - | Output format |
| `JsonSchemaName` | `string?` | - | - | Schema name (required for json_schema) |
| `JsonSchema` | `string?` | - | - | JSON schema definition |
| `JsonSchemaDescription` | `string?` | - | - | Schema description |
| `JsonSchemaIsStrict` | `bool?` | - | `true` | Enforce strict schema |

### Tool/Function Calling

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ToolChoice` | `string?` | "auto", "none", "required" | - | Tool selection behavior |
| `AllowParallelToolCalls` | `bool?` | - | `true` | Enable parallel tool execution |

### Log Probabilities

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `IncludeLogProbabilities` | `bool?` | - | - | Return token log probabilities |
| `TopLogProbabilityCount` | `int?` | 0-20 | - | Number of top tokens to return |
| `LogitBiases` | `Dictionary<int,int>?` | -100 to 100 | - | Token ID → bias mapping |

### Reasoning Models (o1 Series)

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ReasoningEffortLevel` | `string?` | "low", "medium", "high", "minimal" | - | Reasoning effort |

### Audio (gpt-4o-audio-preview)

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ResponseModalities` | `string?` | "text", "audio", "text,audio" | - | Output modalities |
| `AudioVoice` | `string?` | "alloy", "ash", "ballad", "coral", "echo", "sage", "shimmer", "verse" | - | Audio voice |
| `AudioFormat` | `string?` | "wav", "mp3", "flac", "opus", "pcm16" | - | Audio format |

### Service Configuration

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ServiceTier` | `string?` | "auto", "default" | - | Service tier |
| `EndUserId` | `string?` | - | - | End-user identifier |
| `SafetyIdentifier` | `string?` | - | - | Safety/policy identifier |

### Storage & Metadata

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StoredOutputEnabled` | `bool?` | - | Store outputs for distillation/evals |
| `Metadata` | `Dictionary<string,string>?` | - | Dashboard filtering tags |

### Experimental

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WebSearchEnabled` | `bool?` | - | Enable web search |
| `AdditionalProperties` | `Dictionary<string,object>?` | - | Custom model parameters |

## Additional Resources

- [OpenAI Platform](https://platform.openai.com/)
- [OpenAI API Documentation](https://platform.openai.com/docs)
- [OpenAI .NET SDK GitHub](https://github.com/openai/openai-dotnet)
- [Model Documentation](https://platform.openai.com/docs/models)
- [Pricing](https://openai.com/pricing)
- [Best Practices](https://platform.openai.com/docs/guides/production-best-practices)
- [Safety Guidelines](https://platform.openai.com/docs/guides/safety-best-practices)

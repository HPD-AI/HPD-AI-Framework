# Anthropic Provider

**Provider Key:** `anthropic`

## Overview

The Anthropic provider enables HPD-Agent to use Claude models from Anthropic. Claude excels at complex reasoning, coding tasks, creative writing, and multi-step problem solving with industry-leading context windows.

**Key Features:**
-  Extended Thinking mode - See Claude's reasoning process
-  Prompt Caching - Up to 90% cost reduction for repeated contexts
-  Function/tool calling with JSON Schema
-  Vision support for image analysis
-  200K token context window (Claude 3.5)
-  Service tiers for request prioritization
-  Streaming support for real-time responses

**For detailed API documentation, see:**
- [**AnthropicProviderConfig API Reference**](#anthropicproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Anthropic;

// Set API key via environment variable
Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...");

var agent = await new AgentBuilder()
    .WithAnthropic(model: "claude-sonnet-4-5-20250929")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.Anthropic
```

**Dependencies:**
- `Anthropic` - Official Anthropic C# SDK
- `Microsoft.Extensions.AI` - AI abstractions

**Note:** This provider wraps the official [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp) and is **not compatible with Native AOT** due to SDK limitations (see [Native AOT Compatibility](#native-aot-compatibility)).

## Configuration

### Configuration Patterns

The Anthropic provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        configure: opts =>
        {
            opts.MaxTokens = 4096;
            opts.Temperature = 0.7;
            opts.EnablePromptCaching = true;
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`claude-config.json`:**
```json
{
    "name": "ClaudeAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "anthropic",
        "modelName": "claude-sonnet-4-5-20250929",
        "apiKey": "sk-ant-...",
        "providerOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7,\"enablePromptCaching\":true}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("claude-config.json");
```

The `providerOptionsJson` value is a JSON string containing Anthropic-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "ClaudeAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "anthropic",
        ModelName = "claude-sonnet-4-5-20250929",
        ApiKey = "sk-ant-..."
    }
};

config.Provider.SetTypedProviderConfig(new AnthropicProviderConfig
{
    MaxTokens = 4096,
    Temperature = 0.7,
    EnablePromptCaching = true
});

var agent = await config.BuildAsync();
```

#### 4. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "ClaudeAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "anthropic",
        ModelName = "claude-sonnet-4-5-20250929"
    }
};

var anthropicOpts = new AnthropicProviderConfig
{
    MaxTokens = 4096,
    Temperature = 0.7,
    EnablePromptCaching = true
};
config.Provider.SetTypedProviderConfig(anthropicOpts);

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

The `AnthropicProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Parameters

```csharp
configure: opts =>
{
    // Maximum tokens to generate (default: 4096)
    opts.MaxTokens = 4096;
}
```

#### Sampling Parameters

```csharp
configure: opts =>
{
    // Sampling temperature (0.0-1.0, default: 1.0)
    // Use lower values (0.0-0.3) for analytical tasks
    // Use higher values (0.7-1.0) for creative tasks
    opts.Temperature = 0.7;

    // Top-P nucleus sampling (0.0-1.0)
    // You should alter temperature or top_p, but not both
    opts.TopP = 0.9;

    // Top-K sampling - only sample from top K options
    // Removes "long tail" low probability responses
    opts.TopK = 40;

    // Stop sequences - custom text that stops generation
    opts.StopSequences = new List<string> { "STOP", "END" };
}
```

#### Extended Thinking

```csharp
configure: opts =>
{
    // Enable extended thinking mode with token budget
    // Must be >= 1024 and less than max_tokens
    // Shows Claude's reasoning process before the final answer
    opts.ThinkingBudgetTokens = 4096;
}
```

**Extended Thinking** allows you to see Claude's internal reasoning process in special `thinking` content blocks before the final response. This is useful for:
- Complex problem-solving requiring multi-step reasoning
- Debugging Claude's decision-making process
- Educational contexts where reasoning matters

See: [Extended Thinking Documentation](https://docs.anthropic.com/en/docs/build-with-claude/extended-thinking)

#### Service Tier

```csharp
configure: opts =>
{
    // Service tier for request prioritization
    // "auto" (default) - Use priority capacity if available
    // "standard_only" - Only use standard capacity
    opts.ServiceTier = "auto";
}
```

**Service Tiers** control request prioritization:
- **auto**: Automatically uses priority capacity when available, falls back to standard
- **standard_only**: Uses only standard capacity, potentially slower during high load

See: [Service Tiers Documentation](https://docs.anthropic.com/en/api/service-tiers)

#### Prompt Caching

```csharp
configure: opts =>
{
    // Enable prompt caching to reduce costs (up to 90% savings)
    opts.EnablePromptCaching = true;

    // Cache TTL in minutes (1-60, default: 5)
    opts.PromptCacheTTLMinutes = 5;
}
```

**Prompt Caching** dramatically reduces costs and latency by caching frequently used context:
-  Ideal for long system prompts, documentation, or code repositories
-  Reduces costs by up to 90% for cached tokens
-  Reduces latency for repeated contexts
- Cache expires after TTL minutes of inactivity

See: [Prompt Caching Documentation](https://docs.anthropic.com/en/docs/build-with-claude/prompt-caching)

#### Client Middleware

```csharp
.WithAnthropic(
    model: "claude-sonnet-4-5-20250929",
    clientFactory: client => new LoggingChatClient(client, logger))
```

The `clientFactory` parameter allows you to wrap the chat client with middleware for:
- Logging and telemetry
- Caching and memoization
- Request/response transformation
- Custom error handling

## Authentication

Anthropic uses API keys for authentication. The provider supports multiple authentication methods with priority ordering.

### Authentication Priority Order

1. **Explicit API key** in `WithAnthropic()` method
2. **Environment variable**: `ANTHROPIC_API_KEY`
3. **Configuration file**: `appsettings.json` under `"anthropic:ApiKey"` or `"Anthropic:ApiKey"`

### Method 1: Environment Variable (Recommended for Development)

```bash
export ANTHROPIC_API_KEY="sk-ant-..."
```

```csharp
// Automatically uses ANTHROPIC_API_KEY environment variable
var agent = await new AgentBuilder()
    .WithAnthropic(model: "claude-sonnet-4-5-20250929")
    .BuildAsync();
```

### Method 2: Configuration File (Recommended for Production)

**appsettings.json:**
```json
{
    "anthropic": {
        "ApiKey": "sk-ant-..."
    }
}
```

```csharp
// Automatically loads from appsettings.json
var agent = await new AgentBuilder()
    .WithAnthropic(model: "claude-sonnet-4-5-20250929")
    .BuildAsync();
```

### Method 3: Explicit Parameter (Use with Caution)

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        apiKey: "sk-ant-...")
    .BuildAsync();
```

 **Security Warning:** Never hardcode API keys in source code. Use environment variables or configuration files instead.

### Obtaining an API Key

1. Sign up at [console.anthropic.com](https://console.anthropic.com/)
2. Navigate to **API Keys** section
3. Create a new API key
4. Copy the key (starts with `sk-ant-`)

API keys are scoped to workspaces and can be rotated or revoked at any time.

## Supported Models

Anthropic provides multiple Claude model families optimized for different use cases:

### Current Models (Recommended)

| Model ID | Name | Context | Strengths |
|----------|------|---------|-----------|
| `claude-sonnet-4-5-20250929` | Claude 3.5 Sonnet (v2) | 200K | Best balance - intelligence, speed, cost |
| `claude-opus-4-20250514` | Claude 3 Opus | 200K | Highest intelligence, complex reasoning |
| `claude-haiku-3-20240307` | Claude 3 Haiku | 200K | Fastest, most economical |

### Legacy Models

| Model ID | Context | Notes |
|----------|---------|-------|
| `claude-3-5-sonnet-20241022` | 200K | Previous Sonnet version |
| `claude-3-opus-20240229` | 200K | Previous Opus version |
| `claude-2.1` | 200K | Legacy Claude 2 |
| `claude-2.0` | 100K | Legacy Claude 2 |

### Model Selection Guide

**For most use cases:** Use `claude-sonnet-4-5-20250929`
- Best all-around performance
- Excellent at coding, reasoning, and analysis
- Good balance of cost and capability

**For maximum intelligence:** Use `claude-opus-4-20250514`
- Best for complex multi-step reasoning
- Excels at nuanced tasks requiring deep understanding
- Higher cost but highest capability

**For speed and cost:** Use `claude-haiku-3-20240307`
- Fastest responses
- Most economical
- Great for simple tasks or high-volume applications

**For the complete list of models, see:**
[Anthropic Models Documentation](https://docs.anthropic.com/en/docs/models-overview)

## Advanced Features

### Extended Thinking Mode

Extended Thinking allows Claude to show its reasoning process before providing the final answer:

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        configure: opts =>
        {
            opts.ThinkingBudgetTokens = 4096; // Must be >= 1024
        })
    .BuildAsync();

var response = await agent.RunAsync("Solve this complex math problem...");
// Response includes thinking content blocks showing reasoning steps
```

**Benefits:**
-  See Claude's step-by-step reasoning
-  Debug incorrect responses
-  Educational value in showing problem-solving approach
-  Improved accuracy on complex tasks

**Use Cases:**
- Mathematical problem-solving
- Code debugging and optimization
- Multi-step logical reasoning
- Complex decision-making

### Prompt Caching

Prompt caching reduces costs and latency by caching large, frequently-used contexts:

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        configure: opts =>
        {
            opts.EnablePromptCaching = true;
            opts.PromptCacheTTLMinutes = 5; // Cache expires after 5 min of inactivity
        })
    .BuildAsync();

// First call - full cost
var response1 = await agent.RunAsync("Analyze this large document...");

// Subsequent calls within TTL - 90% cost reduction on cached content
var response2 = await agent.RunAsync("What was the main theme?");
```

**Benefits:**
-  Up to 90% cost reduction on cached tokens
-  Reduced latency for repeated contexts
-  Automatic cache management

**Best Practices:**
- Use for large system prompts (documentation, code repositories, style guides)
- Cache stable content that doesn't change frequently
- Set appropriate TTL based on usage patterns

**Caching Behavior:**
- Cache is per-workspace (shared across API keys in the same workspace)
- Cache TTL resets on each access
- Cached content must be at least 1024 tokens
- Maximum 4 cache breakpoints per request

### Service Tiers

Control request prioritization with service tiers:

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        configure: opts =>
        {
            opts.ServiceTier = "auto"; // or "standard_only"
        })
    .BuildAsync();
```

**auto** (default):
- Uses priority capacity when available
- Falls back to standard capacity during high load
- No additional cost
- Best for most use cases

**standard_only**:
- Only uses standard capacity
- May experience slower response times during peak usage
- Guaranteed standard pricing

### Custom Endpoint

Override the default Anthropic API endpoint:

```csharp
var config = new AgentConfig
{
    Provider = new ProviderConfig
    {
        ProviderKey = "anthropic",
        ModelName = "claude-sonnet-4-5-20250929",
        ApiKey = "sk-ant-...",
        Endpoint = "https://custom-proxy.example.com" // Custom endpoint
    }
};
```

**Use Cases:**
- Corporate proxy servers
- Request logging/monitoring proxies
- Anthropic Workbench (local testing)
- Custom rate limiting infrastructure

## Error Handling

The Anthropic provider includes intelligent error classification and automatic retry logic.

### Error Categories

| Category | HTTP Status | Retry Behavior | Examples |
|----------|-------------|----------------|----------|
| **AuthError** | 401, 403 |  No retry | Invalid API key, workspace access denied |
| **RateLimitRetryable** | 429 |  Exponential backoff | Rate limit exceeded, temporary quota |
| **RateLimitTerminal** | 429, 400 |  No retry | Insufficient credits, quota exceeded |
| **ClientError** | 400 |  No retry | Invalid request, malformed JSON |
| **ContextWindow** | 400 |  No retry | Prompt too long, max context exceeded |
| **ServerError** | 500-599 |  Retry | Internal server error, service unavailable |
| **Transient** | - |  Retry | Network errors, connection timeouts |

### Automatic Retry Configuration

The provider automatically retries transient errors with exponential backoff:

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(model: "claude-sonnet-4-5-20250929")
    .WithErrorHandling(config =>
    {
        config.MaxRetryAttempts = 3; // Default
        config.InitialRetryDelay = TimeSpan.FromSeconds(1);
        config.MaxRetryDelay = TimeSpan.FromSeconds(60);
        config.RetryMultiplier = 2.0; // Exponential backoff
    })
    .BuildAsync();
```

### Common Exceptions

#### Invalid API Key (401)
```
authentication_error: Invalid API key
```
**Solution:**
- Verify API key starts with `sk-ant-`
- Check key is copied correctly (no extra spaces)
- Ensure key hasn't been revoked

#### Rate Limit Exceeded (429)
```
rate_limit_error: Number of requests exceeds rate limit
```
**Solution:**
- Automatically retried with exponential backoff
- Check rate limits in [Anthropic Console](https://console.anthropic.com/)
- Consider upgrading tier or implementing request throttling

#### Insufficient Credits (400)
```
invalid_request_error: credit balance is too low
```
**Solution:**
- Add credits in [Anthropic Console](https://console.anthropic.com/)
- This is a terminal error (not retried)

#### Context Length Exceeded (400)
```
invalid_request_error: prompt is too long: X tokens > Y maximum
```
**Solution:**
- Reduce input size or enable prompt caching for large contexts
- Use Claude 3.5 models (200K context)
- Implement conversation summarization

#### Invalid JSON Schema (400)
```
tools.X.custom.input_schema: JSON schema is invalid
```
**Solution:**
- Ensure tool schemas comply with JSON Schema draft 2020-12
- Common issues: using `additionalProperties: false` with `anyOf`, missing `type: "object"` for root schema
- Validate schemas at [jsonschemavalidator.net](https://www.jsonschemavalidator.net/) using draft 2020-12

**Note:** HPD-Agent includes a schema-fixing wrapper to work around a known bug in the Anthropic SDK v12.x that malforms tool schemas. This is transparent to users.

#### Network Errors (AnthropicIOException)
```
Network error: Unable to connect to api.anthropic.com
```
**Solution:**
- Automatically retried with exponential backoff
- Check internet connectivity
- Verify firewall/proxy settings

#### Streaming Errors (AnthropicSseException)
```
SSE parsing error during streaming
```
**Solution:**
- Automatically retried
- Check network stability
- Consider using non-streaming mode for unreliable connections

## Examples

### Example 1: Basic Chat with Claude

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Anthropic;

var agent = await new AgentBuilder()
    .WithAnthropic(model: "claude-sonnet-4-5-20250929")
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

    [Function("Get 5-day weather forecast")]
    public string GetForecast(string location)
    {
        return $"5-day forecast for {location}: Sunny Mon-Wed, rainy Thu-Fri";
    }
}

var agent = await new AgentBuilder()
    .WithAnthropic(model: "claude-sonnet-4-5-20250929")
    .WithToolkit<WeatherToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What's the weather in Seattle and what's the forecast?");
// Claude automatically calls both tools and synthesizes the response
```

### Example 3: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(model: "claude-sonnet-4-5-20250929")
    .BuildAsync();

Console.Write("Claude: ");
await foreach (var chunk in agent.RunAsync("Write a short story about AI."))
{
    Console.Write(chunk);
}
Console.WriteLine();
```

### Example 4: Extended Thinking Mode

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        configure: opts =>
        {
            opts.ThinkingBudgetTokens = 4096; // Enable thinking mode
        })
    .BuildAsync();

var response = await agent.RunAsync(@"
    A farmer needs to cross a river with a fox, a chicken, and a bag of grain.
    The boat can only hold the farmer and one other item.
    If left alone, the fox will eat the chicken, and the chicken will eat the grain.
    How does the farmer get everything across safely?
");

// Response includes thinking blocks showing Claude's reasoning process
Console.WriteLine(response);
```

### Example 5: Prompt Caching for Large Contexts

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        configure: opts =>
        {
            opts.EnablePromptCaching = true;
            opts.PromptCacheTTLMinutes = 10;
        })
    .BuildAsync();

// First call - caches the large documentation
var response1 = await agent.RunAsync(@"
    Here is our 50-page API documentation: [large documentation here...]

    Question: How do I authenticate?
");

// Subsequent calls - 90% cost reduction on cached documentation
var response2 = await agent.RunAsync(@"
    Here is our 50-page API documentation: [same documentation...]

    Question: What rate limits apply?
");
```

### Example 6: Vision - Image Analysis

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(model: "claude-sonnet-4-5-20250929")
    .BuildAsync();

var imageBytes = File.ReadAllBytes("diagram.png");
var imageData = Convert.ToBase64String(imageBytes);

var response = await agent.RunAsync(new[]
{
    new ChatMessage
    {
        Role = ChatRole.User,
        Content = new[]
        {
            new TextContent { Text = "Analyze this architecture diagram:" },
            new ImageContent
            {
                Data = imageData,
                MediaType = "image/png"
            }
        }
    }
});

Console.WriteLine(response);
```

### Example 7: Multi-Turn Conversation with History

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        configure: opts =>
        {
            opts.MaxTokens = 4096;
            opts.Temperature = 0.7;
        })
    .BuildAsync();

var history = new List<ChatMessage>();

// Turn 1
history.Add(new ChatMessage
{
    Role = ChatRole.User,
    Content = "I'm building a REST API"
});

var response1 = await agent.RunAsync(history);
history.Add(new ChatMessage
{
    Role = ChatRole.Assistant,
    Content = response1
});

// Turn 2 - Claude remembers context
history.Add(new ChatMessage
{
    Role = ChatRole.User,
    Content = "What authentication should I use?"
});

var response2 = await agent.RunAsync(history);
Console.WriteLine(response2);
```

### Example 8: Client Middleware for Logging

```csharp
public class LoggingChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly ILogger _logger;

    public LoggingChatClient(IChatClient inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending request to Claude: {MessageCount} messages", messages.Count);
        var start = DateTime.UtcNow;

        var result = await _inner.CompleteAsync(messages, options, cancellationToken);

        var duration = DateTime.UtcNow - start;
        _logger.LogInformation("Received response in {Duration}ms", duration.TotalMilliseconds);

        return result;
    }

    // Implement other IChatClient members...
}

var agent = await new AgentBuilder()
    .WithAnthropic(
        model: "claude-sonnet-4-5-20250929",
        clientFactory: client => new LoggingChatClient(client, logger))
    .BuildAsync();
```

## Troubleshooting

### "Anthropic requires an API key"

**Problem:** Missing API key in configuration.

**Solution:**
```csharp
// Option 1: Environment variable
Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...");

// Option 2: Explicit parameter
.WithAnthropic(model: "...", apiKey: "sk-ant-...")

// Option 3: appsettings.json
{
    "anthropic": {
        "ApiKey": "sk-ant-..."
    }
}
```

### "authentication_error: Invalid API key"

**Problem:** API key is incorrect or revoked.

**Solution:**
1. Verify key starts with `sk-ant-`
2. Check for extra spaces or newlines
3. Generate new key in [Anthropic Console](https://console.anthropic.com/)
4. Ensure key belongs to correct workspace

### "rate_limit_error: Number of requests exceeds rate limit"

**Problem:** Too many requests in a short time period.

**Solution:** The provider automatically retries with exponential backoff. If persistent:
1. Check rate limits in [Anthropic Console](https://console.anthropic.com/)
2. Implement client-side request throttling
3. Consider upgrading to higher tier

### "credit balance is too low"

**Problem:** Insufficient credits in workspace.

**Solution:**
1. Add credits in [Anthropic Console](https://console.anthropic.com/)
2. This is a terminal error (not automatically retried)
3. Set up billing alerts to avoid interruption

### "prompt is too long: X tokens > Y maximum"

**Problem:** Input exceeds model's context window.

**Solution:**
```csharp
// Option 1: Use prompt caching for large contexts
configure: opts =>
{
    opts.EnablePromptCaching = true;
}

// Option 2: Reduce input size
// - Summarize previous conversation turns
// - Remove unnecessary context
// - Split into multiple requests

// Option 3: Use Claude 3.5 (200K context)
.WithAnthropic(model: "claude-sonnet-4-5-20250929")
```

### "Thinking budget tokens must be at least 1024"

**Problem:** ThinkingBudgetTokens set too low.

**Solution:**
```csharp
configure: opts =>
{
    opts.ThinkingBudgetTokens = 1024; // Minimum
    // or
    opts.ThinkingBudgetTokens = 4096; // Recommended
}
```

### "PromptCacheTTLMinutes must be between 1 and 60"

**Problem:** Invalid cache TTL value.

**Solution:**
```csharp
configure: opts =>
{
    opts.PromptCacheTTLMinutes = 5; // Valid: 1-60 minutes
}
```

### "JSON schema is invalid"

**Problem:** Tool schema doesn't comply with JSON Schema draft 2020-12.

**Solution:**
1. HPD-Agent includes an automatic schema-fixing wrapper
2. If still encountering issues, validate schemas at [jsonschemavalidator.net](https://www.jsonschemavalidator.net/)
3. Common issues:
   - Using `additionalProperties: false` with `anyOf`
   - Missing `type: "object"` for root schema
   - Incompatible schema keywords

### Connection timeouts

**Problem:** Requests timing out.

**Solution:**
```csharp
// Option 1: Increase agent timeout
.WithErrorHandling(config =>
{
    config.RequestTimeout = TimeSpan.FromMinutes(3);
})

// Option 2: Reduce max tokens for faster responses
configure: opts =>
{
    opts.MaxTokens = 2048; // Instead of 4096
}

// Option 3: Check network connectivity
// - Verify firewall allows api.anthropic.com
// - Check proxy configuration
// - Test with curl: curl -I https://api.anthropic.com
```

### "You should alter temperature or top_p, but not both"

**Problem:** Both temperature and top_p are set.

**Solution:** Choose one sampling method:
```csharp
configure: opts =>
{
    // Option 1: Use temperature only
    opts.Temperature = 0.7;
    opts.TopP = null;

    // Option 2: Use top_p only
    opts.Temperature = null;
    opts.TopP = 0.9;
}
```

## Native AOT Compatibility

 **The Anthropic provider is NOT compatible with Native AOT deployments.**

### Why Not AOT Compatible?

The official Anthropic C# SDK has **AOT compatibility blockers**.

Making the SDK AOT compatible would require a complete architectural rewrite.

### Alternatives for Native AOT

If you need Native AOT support, use the other providers instead.

HPD-Agent itself is fully Native AOT compatible with comprehensive source generation - only the Anthropic SDK dependency prevents AOT support.

### Contributing

If Native AOT support for Anthropic is critical to your use case, consider contributing to the [upstream Anthropic SDK](https://github.com/anthropics/anthropic-sdk-csharp) to add source generation support.

## ProviderOptionsJson Reference

When configuring via JSON file, all Anthropic-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `AnthropicProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "anthropic",
        "modelName": "claude-sonnet-4-5-20250929",
        "apiKey": "sk-ant-...",
        "providerOptionsJson": "{"maxTokens":4096,"temperature":0.7,"enablePromptCaching":true,"serviceTier":"auto"}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `maxTokens` | int | ≥ 1 | Max tokens to generate (default: 4096) |
| `temperature` | double | 0.0–1.0 | Sampling randomness |
| `topP` | double | 0.0–1.0 | Nucleus sampling threshold |
| `topK` | long | ≥ 1 | Top-K sampling (Anthropic-specific) |
| `stopSequences` | string[] | — | Stop generation sequences |
| `thinkingBudgetTokens` | long | ≥ 1024 | Enable extended thinking with token budget |
| `serviceTier` | string | `"auto"`, `"standard_only"` | Request priority tier |
| `enablePromptCaching` | bool | — | Enable prompt caching to reduce costs |
| `promptCacheTTLMinutes` | int | 1–60 | Cache TTL in minutes (default: 5) |

---

## AnthropicProviderConfig API Reference

### Core Parameters

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxTokens` | `int` | 4096 | Maximum tokens to generate |

### Sampling Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `Temperature` | `double?` | 0.0-1.0 | 1.0 | Randomness in responses (lower = analytical, higher = creative) |
| `TopP` | `double?` | 0.0-1.0 | - | Nucleus sampling threshold (don't use with Temperature) |
| `TopK` | `long?` | - | - | Sample from top K options only |
| `StopSequences` | `List<string>?` | - | - | Custom sequences that stop generation |

### Extended Thinking

| Property | Type | Constraint | Description |
|----------|------|------------|-------------|
| `ThinkingBudgetTokens` | `long?` | ≥ 1024 | Token budget for extended thinking mode |

### Service Tier

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ServiceTier` | `string?` | "auto", "standard_only" | - | Request prioritization tier |

### Prompt Caching

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `EnablePromptCaching` | `bool` | - | `false` | Enable prompt caching for cost reduction |
| `PromptCacheTTLMinutes` | `int?` | 1-60 | 5 | Cache expiration time in minutes |

## Additional Resources

- [Anthropic API Documentation](https://docs.anthropic.com/)
- [Claude Models Overview](https://docs.anthropic.com/en/docs/models-overview)
- [Extended Thinking](https://docs.anthropic.com/en/docs/build-with-claude/extended-thinking)
- [Prompt Caching](https://docs.anthropic.com/en/docs/build-with-claude/prompt-caching)
- [Service Tiers](https://docs.anthropic.com/en/api/service-tiers)
- [Anthropic Console](https://console.anthropic.com/)
- [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp)
- [Rate Limits](https://docs.anthropic.com/en/api/rate-limits)

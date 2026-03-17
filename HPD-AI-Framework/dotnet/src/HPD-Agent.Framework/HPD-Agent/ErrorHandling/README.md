# Provider-Aware Error Handling Architecture

## Overview

HPD-Agent includes a sophisticated, provider-aware error handling system that intelligently classifies errors, determines retry strategies, and respects provider-specific guidance (like Retry-After headers). The system is **opinionated by default** (auto-detects and configures itself) but **flexible when needed** (fully customizable).

## Design Philosophy

**"Opinionated by default, flexible when needed"**

-  Works automatically without any configuration
-  Auto-detects provider and selects appropriate handler
-  Respects provider retry guidance (Retry-After headers)
-  Intelligent error classification (terminal vs retryable)
-  Fully customizable when you need control
-  Native AOT compatible (no reflection)
-  Zero breaking changes to existing code

## Architecture Layers

Error handling is implemented as composable middleware that form a chain of responsibility:

```
┌──────────────────────────────────────────────────────────────┐
│           FunctionRetryMiddleware (Outermost)               │
│  • 3-tier priority retry logic                              │
│  • Provider-aware with Retry-After headers                  │
│  • Emits FunctionRetryEvent for observability               │
└──────────────────────┬───────────────────────────────────────┘
                       │
                       ↓
┌──────────────────────────────────────────────────────────────┐
│           FunctionTimeoutMiddleware (Middle)                │
│  • Enforces timeout per attempt                             │
│  • Throws descriptive TimeoutException                      │
│  • Cancellation-token aware                                 │
└──────────────────────┬───────────────────────────────────────┘
                       │
                       ↓
┌──────────────────────────────────────────────────────────────┐
│         ErrorFormattingMiddleware (Innermost)               │
│  • Security-aware error message formatting                  │
│  • Sanitizes errors by default (prevents info leakage)      │
│  • Stores full exception in context for observability       │
└──────────────────────┬───────────────────────────────────────┘
                       │
                       ↓
┌──────────────────────────────────────────────────────────────┐
│        IProviderErrorHandler Interface (Shared)             │
│  ┌─────────────┬──────────────┬──────────────┬────────────┐    │
│  │   OpenAI    │  Anthropic   │  GoogleAI    │   Ollama   │    │
│  │   Handler   │   Handler    │   Handler    │   Handler  │    │
│  ├─────────────┼──────────────┼──────────────┼────────────┤    │
│  │ AzureAI     │  OpenRouter  │   Bedrock    │  Mistral   │    │
│  │ Inference   │   Handler    │   Handler    │   Handler  │    │
│  ├─────────────┼──────────────┼──────────────┼────────────┤    │
│  │HuggingFace  │ OnnxRuntime  │              │            │    │
│  │ Handler     │   Handler    │              │            │    │
│  └─────────────┴──────────────┴──────────────┴────────────┘    │
│                                                                 │
│               GenericErrorHandler (fallback)                    │
└─────────────────────────────────────────────────────────────────┘
```

## Core Components

### 1. ErrorCategory Enum (`ErrorCategory.cs`)

Classifies errors into actionable categories:

```csharp
public enum ErrorCategory
{
    Unknown,              // Conservative retry
    Transient,           // Network glitches - retry with backoff
    RateLimitRetryable,  // 429 with Retry-After - retry after delay
    RateLimitTerminal,   // Quota exceeded - don't retry
    ClientError,         // 400 - don't retry (bad request)
    AuthError,           // 401 - special handling needed
    ContextWindow,       // Token limit exceeded - don't retry
    ServerError          // 5xx - retry with backoff
}
```

### 2. ProviderErrorDetails (`ProviderErrorDetails.cs`)

Structured error information extracted from exceptions:

```csharp
public class ProviderErrorDetails
{
    public ErrorCategory Category { get; set; }
    public int? StatusCode { get; set; }
    public string? ErrorCode { get; set; }        // e.g., "rate_limit_exceeded"
    public string? ErrorType { get; set; }        // e.g., "insufficient_quota"
    public string Message { get; set; }
    public TimeSpan? RetryAfter { get; set; }     // From Retry-After header
    public string? RequestId { get; set; }        // For debugging
    public Dictionary<string, object>? RawDetails { get; set; }
}
```

### 3. IProviderErrorHandler Interface (`IProviderErrorHandler.cs`)

Contract for provider-specific error handling:

```csharp
public interface IProviderErrorHandler
{
    // Parse exception into structured details
    ProviderErrorDetails? ParseError(Exception exception);

    // Calculate retry delay (returns null if non-retryable)
    TimeSpan? GetRetryDelay(
        ProviderErrorDetails details,
        int attempt,
        TimeSpan initialDelay,
        double multiplier,
        TimeSpan maxDelay);

    // Check if special handling needed (e.g., token refresh)
    bool RequiresSpecialHandling(ProviderErrorDetails details);
}
```

### 4. FunctionRetryMiddleware (`FunctionRetryMiddleware.cs`)

Implements the 3-tier retry logic:

- Catches exceptions during function execution
- Applies priority system: custom strategy → provider-aware → exponential backoff
- Respects provider Retry-After headers
- Enforces per-category retry limits
- Emits `FunctionRetryEvent` (defined in `AgentEvents.cs`) for observability
- Uses exponential backoff with jitter (±10%)

```csharp
public class FunctionRetryMiddleware : IAgentMiddleware
{
    private readonly ErrorHandlingConfig _config;
    private readonly IProviderErrorHandler _providerHandler;

    public async ValueTask<object?> ExecuteFunctionAsync(
        AgentMiddlewareContext context,
        Func<ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        // Retry loop with 3-tier logic
    }
}
```

**Event Emitted:**
```csharp
// From AgentEvents.cs - Observability Events
public record FunctionRetryEvent(
    string FunctionName,
    int Attempt,
    int MaxRetries,
    TimeSpan Delay,
    Exception Exception,
    string ExceptionType,
    string ErrorMessage
) : AgentEvent, IObservabilityEvent;
```

### 5. FunctionTimeoutMiddleware (`FunctionTimeoutMiddleware.cs`)

Enforces timeout on function execution:

- Wraps function call in `Task.WaitAsync(timeout)`
- Respects cancellation tokens
- Throws descriptive `TimeoutException` with function name and duration
- Positioned inside RetryMiddleware  (retries happen before timeout)

```csharp
public class FunctionTimeoutMiddleware : IAgentMiddleware
{
    private readonly TimeSpan _timeout;

    public async ValueTask<object?> ExecuteFunctionAsync(
        AgentMiddlewareContext context,
        Func<ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        // Enforce timeout using Task.WaitAsync
    }
}
```

### 6. ErrorFormattingMiddleware (`ErrorFormattingMiddleware.cs`)

Formats errors for secure LLM consumption:

- **Default behavior**: Returns generic error message (secure)
- **Optional behavior**: Returns detailed error message (configurable)
- **Always logs**: Stores full exception in `context.FunctionException` for observability
- Prevents exposing: stack traces, connection strings, paths, API keys

Controlled by `ErrorHandlingConfig.IncludeDetailedErrorsInChat`:
- `false` (default): `"Error: Function 'X' failed."`
- `true` (trusted only): `"Error invoking function 'X': {exception.Message}"`

```csharp
public class ErrorFormattingMiddleware : IAgentMiddleware
{
    private readonly bool _includeDetailedErrors;

    public async ValueTask<object?> ExecuteFunctionAsync(
        AgentMiddlewareContext context,
        Func<ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            // Store for observability, format for LLM
            context.FunctionException = ex;
            // Return sanitized or detailed message based on config
        }
    }
}
```

## Provider Handlers

### OpenAIErrorHandler (`HPD-Agent.Providers.OpenAI/OpenAIErrorHandler.cs`)

Handles both OpenAI and Azure OpenAI errors:

**Capabilities:**
-  Parses `HttpRequestException` (OpenAI SDK)
-  Parses `Azure.RequestFailedException` (Azure SDK)
-  Extracts Retry-After from headers
-  Parses retry delays from messages: `"Please try again in 1.898s"`
-  Detects context window errors: `"context_length_exceeded"`
-  Detects terminal quota: `"insufficient_quota"`
-  Extracts request IDs for debugging

**Native AOT Compatibility:**
- Uses regex to parse Azure exception messages (no reflection)
- Pattern: `"Service request failed.\nStatus: 429 (Too Many Requests)"`
- Extracts status code, request ID, and retry delay from message text

### AnthropicErrorHandler (`HPD-Agent.Providers.Anthropic/AnthropicErrorHandler.cs`)

Handles Anthropic Claude API errors:

**Capabilities:**
-  Parses rate limit errors
-  Extracts retry delay from error messages
-  Classifies terminal quota errors
-  Handles authentication errors

### GoogleAIErrorHandler (`HPD-Agent.Providers.GoogleAI/GoogleAIErrorHandler.cs`)

Handles Google AI (Gemini) API errors:

**Capabilities:**
-  Parses quota exceeded errors
-  Detects resource exhausted errors
-  Extracts backend error information
-  Handles safety/policy blocked errors

### OllamaErrorHandler (`HPD-Agent.Providers.Ollama/OllamaErrorHandler.cs`)

Handles local Ollama model errors:

**Capabilities:**
-  Detects model loading in progress
-  Parses connection refused errors
-  Handles out-of-memory errors
-  Recognizes model not found errors

### OpenRouterErrorHandler (`HPD-Agent.Providers.OpenRouter/OpenRouterErrorHandler.cs`)

Handles OpenRouter API errors:

**Capabilities:**
-  Parses rate limit and quota errors
-  Extracts queue/busy states
-  Handles insufficient credits
-  Detects model unavailability

### BedrockErrorHandler (`HPD-Agent.Providers.Bedrock/BedrockErrorHandler.cs`)

Handles AWS Bedrock errors:

**Capabilities:**
-  Parses access denied errors
-  Detects throttling and rate limits
-  Handles model not ready errors
-  Extracts service quota information

### AzureAIInferenceErrorHandler (`HPD-Agent.Providers.AzureAIInference/AzureAIInferenceErrorHandler.cs`)

Handles Azure AI Inference errors:

**Capabilities:**
-  Parses throttling errors
-  Detects resource busy states
-  Handles quota exceeded errors
-  Extracts authentication failures

### MistralErrorHandler (`HPD-Agent.Providers.Mistral/MistralErrorHandler.cs`)

Handles Mistral AI API errors:

**Capabilities:**
-  Parses rate limit errors
-  Detects service overload
-  Handles quota exceeded errors
-  Extracts authentication failures

### HuggingFaceErrorHandler (`HPD-Agent.Providers.HuggingFace/HuggingFaceErrorHandler.cs`)

Handles HuggingFace API errors:

**Capabilities:**
-  Detects model loading in progress
-  Parses estimated time to ready
-  Handles rate limit errors
-  Detects service unavailability

### OnnxRuntimeErrorHandler (`HPD-Agent.Providers.OnnxRuntime/OnnxRuntimeErrorHandler.cs`)

Handles ONNX Runtime model errors:

**Capabilities:**
-  Detects model initialization failures
-  Handles out-of-memory errors
-  Parses invalid input errors
-  Detects execution provider issues

### GenericErrorHandler (`ErrorHandling/GenericErrorHandler.cs`)

Fallback handler for unknown providers:

**Capabilities:**
-  Extracts HTTP status from `HttpRequestException`
-  Parses status codes from exception messages (AOT-safe)
-  Basic classification (400→ClientError, 429→RateLimitRetryable, 5xx→ServerError)
-  Exponential backoff with jitter

**Message Parsing Patterns:**
```regex
Status:\s*(\d{3})      // "Status: 429"
\((\d{3})\)           // "(429)" or "Error (429)"
```

## Configuration

### ErrorHandlingConfig (`AgentConfig.cs`)

All error handling settings in one place:

```csharp
public class ErrorHandlingConfig
{
    // Existing properties (backward compatible)
    public bool NormalizeErrors { get; set; } = true;
    public bool IncludeProviderDetails { get; set; } = false;
    public int MaxRetries { get; set; } = 3;
    public TimeSpan? SingleFunctionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    // NEW: Provider-aware settings
    public bool UseProviderRetryDelays { get; set; } = true;
    public bool AutoRefreshTokensOn401 { get; set; } = true;
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    public double BackoffMultiplier { get; set; } = 2.0;

    // Per-category retry limits (optional)
    public Dictionary<ErrorCategory, int>? MaxRetriesByCategory { get; set; }

    // Custom handler (advanced)
    [JsonIgnore]
    public IProviderErrorHandler? ProviderHandler { get; set; }

    // Custom strategy (expert mode)
    [JsonIgnore]
    public Func<Exception, int, CancellationToken, Task<TimeSpan?>>? CustomRetryStrategy { get; set; }
}
```

## How It Works

### Middleware Composition

Error handling is implemented as three composable middleware layers that work together:

```csharp
// Middleware are registered in order (outermost to innermost)
.WithFunctionRetry()      // Outermost - retry entire operation
.WithFunctionTimeout()    // Middle - timeout individual attempts
.WithErrorFormatting()    // Innermost - format errors for LLM
```

Each middleware wraps the next, creating a chain of responsibility:
1. **FunctionRetryMiddleware** catches exceptions and decides whether to retry
2. **FunctionTimeoutMiddleware** enforces timeout on each attempt
3. **ErrorFormattingMiddleware** formats errors securely for the LLM
4. **Actual function execution** happens at the end of the chain

### FunctionRetryMiddleware - 3-Tier Retry Logic

When an exception occurs during function execution:

```
┌─────────────────────────────────────────────────────────┐
│ PRIORITY 1: Custom Retry Strategy                      │
│ If user provided CustomRetryStrategy delegate          │
│ → Use it (full control)                                │
└─────────────────────────────────────────────────────────┘
                        ↓ (if null)
┌─────────────────────────────────────────────────────────┐
│ PRIORITY 2: Provider-Aware Handling                    │
│ 1. Parse exception with IProviderErrorHandler          │
│ 2. Check per-category retry limits                     │
│ 3. Get provider-calculated delay:                      │
│    - Use RetryAfter if present (respects headers)      │
│    - Use exponential backoff with provider settings    │
│    - Return null if error is non-retryable             │
└─────────────────────────────────────────────────────────┘
                        ↓ (if null)
┌─────────────────────────────────────────────────────────┐
│ PRIORITY 3: Exponential Backoff (Fallback)             │
│ delay = base * 2^attempt * random(0.9-1.1)             │
│ Apply MaxRetryDelay cap                                │
└─────────────────────────────────────────────────────────┘
```

Features:
-  Respects provider Retry-After headers (e.g., OpenAI 429 responses)
-  Per-error-category retry limits (e.g., more retries for rate limits)
-  Intelligent error classification (transient, rate limit, etc.)
-  Exponential backoff with jitter to avoid thundering herd
-  Emits `FunctionRetryEvent` for observability

### FunctionTimeoutMiddleware - Timeout Enforcement

Enforces a timeout on each function execution attempt:

-  Uses `Task.WaitAsync()` for clean timeout handling
-  Wrapped by RetryMiddleware  (retries happen before timeout)
-  Throws descriptive `TimeoutException` with function name and delay
-  Respects cancellation tokens

### ErrorFormattingMiddleware - Security-Aware Error Formatting

Formats exceptions for safe LLM consumption:

-  **Default (secure)**: Returns generic message like `"Error: Function 'X' failed."`
-  **Optional (detailed)**: Returns full exception message (configurable)
-  **Always observability**: Stores full exception in `context.FunctionException` for logging
-  Prevents exposing: stack traces, connection strings, file paths, API keys

Controlled by `ErrorHandlingConfig.IncludeDetailedErrorsInChat`:
```csharp
false // Default - secure, generic errors
true  // Only in trusted environments - includes exception details
```

### Auto-Detection

When an agent is created, it automatically selects the appropriate error handler:

```csharp
// In Agent constructor
if (config.ErrorHandling != null && config.ErrorHandling.ProviderHandler == null)
{
    config.ErrorHandling.ProviderHandler = CreateProviderHandler(config.Provider?.Provider);
}

private static IProviderErrorHandler CreateProviderHandler(ChatProvider? provider)
{
    return provider switch
    {
        ChatProvider.OpenAI => new OpenAIErrorHandler(),
        ChatProvider.AzureOpenAI => new OpenAIErrorHandler(),
        ChatProvider.Anthropic => new AnthropicErrorHandler(),
        ChatProvider.GoogleAI => new GoogleAIErrorHandler(),
        ChatProvider.Ollama => new OllamaErrorHandler(),
        ChatProvider.OpenRouter => new OpenRouterErrorHandler(),
        ChatProvider.Bedrock => new BedrockErrorHandler(),
        ChatProvider.AzureAIInference => new AzureAIInferenceErrorHandler(),
        ChatProvider.Mistral => new MistralErrorHandler(),
        ChatProvider.HuggingFace => new HuggingFaceErrorHandler(),
        ChatProvider.OnnxRuntime => new OnnxRuntimeErrorHandler(),
        _ => new GenericErrorHandler()
    };
}
```

### Example Flow

```
1. Tool calls OpenAI API → HttpRequestException (429)
2. FunctionRetryMiddleware catches exception
3. Checks CustomRetryStrategy → null
4. Calls OpenAIErrorHandler.ParseError()
   → Returns ProviderErrorDetails {
       Category = RateLimitRetryable,
       StatusCode = 429,
       RetryAfter = TimeSpan.FromSeconds(2),
       ErrorCode = "rate_limit_exceeded"
   }
5. Checks per-category limit → attempt 1 < 3
6. Calls OpenAIErrorHandler.GetRetryDelay()
   → Returns TimeSpan.FromSeconds(2) (from RetryAfter)
7. Emits FunctionRetryEvent
8. Waits 2 seconds
9. FunctionTimeoutMiddleware enforces timeout on retry
10. ErrorFormattingMiddleware ready to format if retry fails
11. Retries tool call → Success!
```

## Usage Examples

### Default Usage (Auto-Magic)

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI("gpt-4", apiKey)
    // Middleware are added automatically by builder
    .BuildAsync();

// Error handling works automatically:
// - Auto-detects OpenAI provider
// - Respects Retry-After headers
// - Intelligent retry decisions
// - Timeouts enforced
// - Errors sanitized for LLM
// - No configuration needed!
```

### Explicit Middleware Registration

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI("gpt-4", apiKey)
    // Register middleware explicitly (order matters!)
    .WithFunctionRetry()      // Outermost - retry entire operation
    .WithFunctionTimeout()    // Middle - timeout each attempt
    .WithErrorFormatting()    // Innermost - format errors
    .BuildAsync();
```

### Custom Error Formatting (Detailed Errors for Trusted Environments)

```csharp
var config = new AgentConfig
{
    ErrorHandling = new ErrorHandlingConfig
    {
        IncludeDetailedErrorsInChat = true  //   Only in trusted environments
    }
};

var agent = await new AgentBuilder(config)
    .WithOpenAI("gpt-4", apiKey)
    .BuildAsync();
```

### Custom Per-Category Limits

```csharp
var config = new AgentConfig
{
    ErrorHandling = new ErrorHandlingConfig
    {
        MaxRetries = 3,  // Default for all
        MaxRetriesByCategory = new Dictionary<ErrorCategory, int>
        {
            [ErrorCategory.RateLimitRetryable] = 5,  // More retries for rate limits
            [ErrorCategory.ServerError] = 2           // Fewer for server errors
        }
    }
};
```

### Custom Retry Strategy (Expert Mode)

```csharp
config.ErrorHandling.CustomRetryStrategy = async (exception, attempt, ct) =>
{
    if (exception is HttpRequestException httpEx && httpEx.StatusCode == HttpStatusCode.TooManyRequests)
    {
        // Custom logic: wait longer on rate limits
        return TimeSpan.FromSeconds(10 * (attempt + 1));
    }

    // Let provider handler decide
    return null;
};
```

### Custom Provider Handler

```csharp
// Implement your own handler
public class MyCustomHandler : IProviderErrorHandler
{
    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // Your custom parsing logic
    }

    // ... implement other methods
}

// Use it
config.ErrorHandling.ProviderHandler = new MyCustomHandler();
```

### Custom Timeout

```csharp
var config = new AgentConfig
{
    ErrorHandling = new ErrorHandlingConfig
    {
        SingleFunctionTimeout = TimeSpan.FromSeconds(60)  // Per-attempt timeout
    }
};
```

### Disable Error Handling (Unit Tests)

```csharp
config.ErrorHandling.MaxRetries = 0;  // No retries
```

## Native AOT Compatibility

### Why No Reflection?

Native AOT doesn't support reflection APIs like `GetProperty()`, `GetMethod()`, or `Invoke()`. We use AOT-safe alternatives:

### Azure Exception Parsing (Before)
```csharp
//    NOT AOT-compatible
var statusProp = exception.GetType().GetProperty("Status");
var status = (int?)statusProp.GetValue(exception);
```

### Azure Exception Parsing (After)
```csharp
//  AOT-compatible
var message = exception.Message;
// Azure format: "Service request failed.\nStatus: 429 (Too Many Requests)"
var statusMatch = Regex.Match(message, @"Status:\s*(\d{3})");
var status = int.Parse(statusMatch.Groups[1].Value);
```

### Type Checking (Safe)
```csharp
//  GetType().FullName is AOT-safe
if (exception.GetType().FullName == "Azure.RequestFailedException")
{
    // Parse the message
}
```

### Pattern Matching (Safe)
```csharp
//  Pattern matching is AOT-safe
if (exception is HttpRequestException httpEx)
{
    var status = httpEx.StatusCode;
}
```

## Adding New Provider Handlers

### Step 1: Create Handler Class

```csharp
namespace HPD.Agent.ErrorHandling.Providers;

internal class AnthropicErrorHandler : IProviderErrorHandler
{
    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // Parse Anthropic-specific exceptions
        // Use message parsing for AOT compatibility
    }

    public TimeSpan? GetRetryDelay(ProviderErrorDetails details, ...)
    {
        // Anthropic-specific retry logic
        // Check for x-should-retry header
        // Use exponential backoff
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
    {
        return details.Category == ErrorCategory.AuthError;
    }
}
```

### Step 2: Register in Agent.cs

```csharp
private static IProviderErrorHandler CreateProviderHandler(ChatProvider? provider)
{
    return provider switch
    {
        ChatProvider.OpenAI => new OpenAIErrorHandler(),
        ChatProvider.AzureOpenAI => new OpenAIErrorHandler(),
        ChatProvider.Anthropic => new AnthropicErrorHandler(),  // Add here
        _ => new GenericErrorHandler()
    };
}
```

### Step 3: Test with Message Parsing

```csharp
[Fact]
public void ParseError_AnthropicException_ExtractsDetails()
{
    // Anthropic exceptions have consistent message formats
    var exception = new Exception("Error: rate_limit_error (429): Rate limit exceeded");
    var handler = new AnthropicErrorHandler();

    var details = handler.ParseError(exception);

    Assert.Equal(429, details.StatusCode);
    Assert.Equal(ErrorCategory.RateLimitRetryable, details.Category);
}
```

## Observability

### Events

Error handling middleware emits observability events for monitoring and debugging:

- **`FunctionRetryEvent`** (`AgentEvents.cs`) - Emitted by `FunctionRetryMiddleware` when a function call is being retried
  - Includes: FunctionName, Attempt, MaxRetries, Delay, Exception details
  - Implements: `IObservabilityEvent` for proper event classification
  - Use case: Monitor retry patterns, detect flaky functions, measure resilience

Example event handler:
```csharp
void OnFunctionRetryEvent(FunctionRetryEvent evt)
{
    logger.LogWarning(
        "Retrying function {Function} (attempt {Attempt}/{Max}) after {Delay}ms: {Error}",
        evt.FunctionName,
        evt.Attempt,
        evt.MaxRetries,
        evt.Delay.TotalMilliseconds,
        evt.ErrorMessage);
}
```

## Best Practices

### For Library Users

1. **Trust the defaults**: Auto-detection works for 95% of cases
2. **Configure per-category limits**: If you need finer control
3. **Use CustomRetryStrategy**: Only when you have very specific requirements
4. **Log request IDs**: Use `ProviderErrorDetails.RequestId` for debugging
5. **Monitor retry patterns**: Track which errors are retried most

### For Contributors

1. **Parse messages, not properties**: Use regex for AOT compatibility
2. **Test message parsing**: Exception messages are less reliable than properties
3. **Provide fallbacks**: Gracefully degrade if parsing fails
4. **Document message formats**: Help future maintainers understand patterns
5. **Use source generators**: For regex when possible (`[GeneratedRegex]`)

## Troubleshooting

### Error: "Non-retryable error after 1 attempt"

**Cause**: Error classified as terminal (ClientError, ContextWindow, RateLimitTerminal)

**Solution**: Check error message for details. These errors usually need code changes, not retries.

### Error: "Maximum consecutive errors exceeded"

**Cause**: Tool keeps failing across multiple agent iterations

**Solution**:
- Check if the tool itself has bugs
- Increase `MaxRetries` if transient issues are common
- Add custom retry strategy for specific error patterns

### Provider delays not respected

**Cause**: `UseProviderRetryDelays = false` in config

**Solution**:
```csharp
config.ErrorHandling.UseProviderRetryDelays = true;  // Default
```

### Retries happening too fast

**Cause**: Provider's Retry-After not being parsed correctly

**Solution**:
1. Check exception message format
2. Add logging to see what `RetryAfter` value is extracted
3. File issue with message format for investigation

## Performance Considerations

### Regex Performance

-  Uses `[GeneratedRegex]` for .NET 7+ (compiled regex)
-  Regex is only used on error paths (not hot path)
-  Patterns are simple and optimized

### Memory Allocation

-  Error handlers are cached (one per agent instance)
-  No allocations on success path
-  Minimal allocations on error path (exception already thrown)

### Retry Delays

-  Default max delay: 30 seconds (configurable)
-  Exponential backoff prevents thundering herd
-  Jitter (±10%) distributes load

## Future Enhancements

### Potential Features

- [ ] Circuit breaker per provider (fail fast after N errors)
- [ ] Retry budget (max retries per time window)
- [ ] Telemetry integration (OpenTelemetry spans for retries)
- [ ] Retry metrics (success rate, average delay, etc.)
- [ ] Adaptive backoff (learn optimal delays over time)
- [ ] VertexAI error handler (Google Cloud Vertex AI)

### Provider Coverage

 **11 Provider Handlers Implemented:**
- OpenAI (including Azure OpenAI)
- Anthropic (Claude)
- Google AI (Gemini)
- Ollama (Local models)
- OpenRouter (Multi-provider)
- AWS Bedrock
- Azure AI Inference
- Mistral AI
- HuggingFace
- ONNX Runtime
- Generic fallback for unknown providers

## Migration Guide

### From Basic Error Handling

**Before:**
```csharp
var agent = await new AgentBuilder()
    .WithOpenAI("gpt-4", apiKey)
    .BuildAsync();

// Basic retries with exponential backoff
```

**After:**
```csharp
var agent = await new AgentBuilder()
    .WithOpenAI("gpt-4", apiKey)
    .BuildAsync();

// Same code, but now:
//  Respects Retry-After headers
//  Classifies errors intelligently
//  Doesn't retry terminal errors
//  Extracts debugging info
```

**No code changes required!** 🎉

## Summary

HPD-Agent's error handling system is implemented as composable middleware providing:

 **Intelligent Classification**: Categorizes errors (transient, rate limit, client error, etc.)
 **Provider-Aware**: Understands OpenAI, Azure, and others with fallback
 **Respectful**: Honors Retry-After headers and provider guidance
 **Composable**: Three separate middleware (retry, timeout, formatting)
 **Automatic**: Works out of the box with zero configuration
 **Flexible**: Fully customizable for advanced scenarios
 **Secure**: Sanitizes errors by default, logs full exceptions separately
 **Observable**: Emits events for logging and monitoring
 **AOT-compatible**: No reflection, works with Native AOT
 **Battle-tested**: Patterns from Gemini CLI and Codex CLI
 **Zero breaking changes**: Existing code works unchanged

The system follows the philosophy: **"Be opinionated by default, but flexible when needed"** - it just works for most users, but power users have full control when they need it.

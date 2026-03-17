# Error Handling

`ErrorHandlingConfig` controls how the agent handles transient errors, retries, and what information gets surfaced to the LLM when a tool or provider call fails.

```csharp
var config = new AgentConfig
{
    ErrorHandling = new ErrorHandlingConfig
    {
        MaxRetries = 3,
        SingleFunctionTimeout = TimeSpan.FromSeconds(30)
    }
};
```

---

## Properties

### Retries

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Number of retries for transient provider/tool errors |
| `RetryDelay` | `TimeSpan` | `1 second` | Initial delay before the first retry (exponential backoff) |
| `MaxRetryDelay` | `TimeSpan` | `30 seconds` | Maximum backoff delay cap |
| `BackoffMultiplier` | `double` | `2.0` | Exponential backoff multiplier |
| `UseProviderRetryDelays` | `bool` | `true` | Honour `Retry-After` headers from the provider |
| `AutoRefreshTokensOn401` | `bool` | `true` | Automatically refresh auth tokens on 401 responses |
| `MaxRetriesByCategory` | `Dictionary<ErrorCategory, int>?` | `null` | Per-category retry limits (overrides `MaxRetries` for that category) |
| `CustomRetryStrategy` | `Func<Exception, int, CancellationToken, Task<TimeSpan?>>?` | `null` | Fully custom async retry logic — return `null` to use the default, return a `TimeSpan` to wait, or throw to abort |

### Timeouts

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SingleFunctionTimeout` | `TimeSpan?` | `30 seconds` | Maximum time allowed for a single tool function call |

### Error Formatting

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NormalizeErrors` | `bool` | `true` | Normalize provider error responses into a consistent format |
| `IncludeProviderDetails` | `bool` | `false` | Include provider-specific error details in the normalized message |
| `IncludeDetailedErrorsInChat` | `bool` | `false` | Send full exception messages to the LLM (**security risk** — only enable in controlled environments) |

---

## Examples

### Conservative setup (production)

```csharp
ErrorHandling = new ErrorHandlingConfig
{
    MaxRetries = 2,
    RetryDelay = TimeSpan.FromSeconds(2),
    MaxRetryDelay = TimeSpan.FromSeconds(60),
    SingleFunctionTimeout = TimeSpan.FromSeconds(20),
    NormalizeErrors = true,
    IncludeDetailedErrorsInChat = false
}
```

### Debug setup (development)

```csharp
ErrorHandling = new ErrorHandlingConfig
{
    MaxRetries = 0,                          // Fail fast
    IncludeDetailedErrorsInChat = true,      // Show full errors to the LLM
    IncludeProviderDetails = true
}
```

### Per-category retry limits

```csharp
ErrorHandling = new ErrorHandlingConfig
{
    MaxRetries = 3,
    MaxRetriesByCategory = new Dictionary<ErrorCategory, int>
    {
        [ErrorCategory.RateLimit] = 5,       // More retries for rate limits
        [ErrorCategory.Authentication] = 0   // Never retry auth errors
    }
}
```

### Custom retry strategy

```csharp
ErrorHandling = new ErrorHandlingConfig
{
    CustomRetryStrategy = async (exception, attempt, ct) =>
    {
        if (exception is MyTransientException)
            return TimeSpan.FromSeconds(attempt * 2);  // Linear backoff for this type

        return null;  // Fall back to default strategy for everything else
    }
}
```

---

## JSON Example

```json
{
    "ErrorHandling": {
        "MaxRetries": 3,
        "RetryDelay": "00:00:01",
        "MaxRetryDelay": "00:00:30",
        "BackoffMultiplier": 2.0,
        "SingleFunctionTimeout": "00:00:30",
        "UseProviderRetryDelays": true,
        "AutoRefreshTokensOn401": true,
        "NormalizeErrors": true,
        "IncludeProviderDetails": false,
        "IncludeDetailedErrorsInChat": false
    }
}
```

> `CustomRetryStrategy` and `MaxRetriesByCategory` cannot be expressed in JSON — set them programmatically on the `AgentConfig` object.

---

## See Also

- [Agent Config](Agent%20Config.md) — full config reference
- [Agentic Loop](Agent%20Config.md#nested-config-sections) — turn-level timeouts (`AgenticLoopConfig.MaxTurnDuration`)

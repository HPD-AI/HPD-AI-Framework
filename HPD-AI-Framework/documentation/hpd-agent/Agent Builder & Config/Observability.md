# Observability

HPD-Agent has two layers of observability:

1. **LLM-level** — OpenTelemetry tracing and `ILogger` structured logging via the `Microsoft.Extensions.AI` integration. These instrument the underlying `IChatClient` calls.
2. **Agent-level** — HPD-specific event observers and handlers that see typed agent events (`TextDeltaEvent`, `ToolCallEvent`, `IterationCompleteEvent`, etc.).

Both layers are configured via `AgentBuilder` methods. The `ObservabilityConfig` section on `AgentConfig` controls the agent-level event system behavior.

---

## Builder Methods

### Logging

```csharp
// Auto-discover ILoggerFactory from the service provider
.WithLogging()
.WithLogging(enableSensitiveData: true)

// Provide an explicit logger factory
.WithLogging(loggerFactory)
.WithLogging(loggerFactory, new LoggingMiddlewareOptions { ... })
```

### Tracing (OpenTelemetry)

```csharp
// Auto-configure tracing
.WithTracing()

// With a custom source name and sanitizer
.WithTracing("MyServiceName", new SpanSanitizerOptions
{
    SanitizePromptContent = true
})
```

### Telemetry (logging + tracing together)

```csharp
.WithTelemetry()
.WithTelemetry("MySourceName", enableSensitiveData: false)
```

### Agent-level observers and handlers

```csharp
// Fire-and-forget — does not block the agent event stream
.WithObserver(myObserver)       // implements IAgentEventObserver

// Synchronous — executes in order, can inspect/transform events
.WithEventHandler(myHandler)    // implements IAgentEventHandler
```

---

## `ObservabilityConfig`

Controls the agent-level event system, including event sampling and the circuit breaker that protects observers from causing cascading failures.

```csharp
var config = new AgentConfig
{
    Observability = new ObservabilityConfig
    {
        EmitObservabilityEvents = true
    }
};
```

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EmitObservabilityEvents` | `bool` | `false` | Emit `IObservabilityEvent` events to registered observers |
| `EnableSampling` | `bool` | `false` | Enable sampling for high-frequency delta events |
| `TextDeltaSamplingRate` | `double` | `1.0` | Fraction of `TextDeltaEvent`s to emit (0.0 = none, 1.0 = all) |
| `ReasoningDeltaSamplingRate` | `double` | `1.0` | Fraction of `ReasoningDeltaEvent`s to emit |
| `MaxConcurrentObservers` | `int` | `10` | Maximum observers notified concurrently per event |
| `MaxConsecutiveFailures` | `int` | `10` | Number of consecutive observer failures before the circuit breaker opens |
| `SuccessesToResetCircuitBreaker` | `int` | `3` | Successful notifications needed to close the circuit breaker again |

### Sampling

Sampling is useful when streaming at high frequency and you don't need every delta in downstream analytics:

```csharp
Observability = new ObservabilityConfig
{
    EnableSampling = true,
    TextDeltaSamplingRate = 0.1,          // Emit only 10% of text deltas
    ReasoningDeltaSamplingRate = 0.05     // Emit only 5% of reasoning deltas
}
```

Non-delta events (tool calls, errors, iteration complete, etc.) are never sampled.

### Circuit Breaker

If observers throw exceptions repeatedly, the circuit breaker opens to prevent observer failures from degrading the agent. Once open, events are dropped until the observer succeeds `SuccessesToResetCircuitBreaker` times consecutively.

```csharp
Observability = new ObservabilityConfig
{
    MaxConsecutiveFailures = 5,
    SuccessesToResetCircuitBreaker = 2
}
```

---

## JSON Example

```json
{
    "Observability": {
        "EmitObservabilityEvents": true,
        "EnableSampling": false,
        "TextDeltaSamplingRate": 1.0,
        "ReasoningDeltaSamplingRate": 1.0,
        "MaxConcurrentObservers": 10,
        "MaxConsecutiveFailures": 10,
        "SuccessesToResetCircuitBreaker": 3
    }
}
```

---

## See Also

- [Agent Config](Agent%20Config.md)
- [Events](../Events/05.1%20Events%20Overview.md) — typed event reference and observer/handler patterns

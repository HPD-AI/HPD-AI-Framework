# Agent Builder

`AgentBuilder` is the fluent API for constructing an `Agent`. It accepts an optional `AgentConfig` as the base and lets you layer runtime-only concerns on top via chained `.With*()` methods.

```csharp
var agent = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .WithToolkit<MyToolkit>()
    .WithLogging(LogLevel.Information)
    .BuildAsync();
```

Builder methods **override** values from the config — if `config.Provider` is set and you also call `.WithOpenAI(...)`, the builder value wins.

---

## Constructors

```csharp
// 1. Blank config — provider must be supplied via a With*() method
new AgentBuilder()

// 2. From a config object
new AgentBuilder(AgentConfig config)

// 3. From a JSON file path — deserializes, then applies as base config
new AgentBuilder(string jsonFilePath)

// 4. Static factory — equivalent to constructor 3
AgentBuilder.FromJsonFile(string jsonFilePath)

// 5. Static factory — blank config
AgentBuilder.Create()
```

---

## Provider

### Named provider (from registry)

```csharp
.WithProvider("openai", "gpt-4o", apiKey: "sk-...")
```

### Provider-specific convenience methods

Each provider package adds a dedicated extension method:

```csharp
.WithOpenAI("gpt-4o", apiKey: "sk-...")
.WithAnthropic("claude-sonnet-4-6", apiKey: "sk-ant-...")
.WithOllama("llama3.2")
.WithAzureAI("gpt-4o", endpoint: "https://...", apiKey: "...")
.WithGoogleAI("gemini-2.0-flash", apiKey: "...")
.WithBedrock("anthropic.claude-3-5-sonnet-20241022-v2:0")
.WithMistral("mistral-large-latest", apiKey: "...")
.WithHuggingFace("meta-llama/Meta-Llama-3-8B-Instruct", apiKey: "...")
```

See [Providers](Providers/00%20Providers%20Overview.md) for full signatures and provider-specific config.

### Existing chat client

```csharp
// Bypass the provider registry and use an IChatClient directly
.WithChatClient(myChatClient)

// Defer provider setup (used internally in multi-agent workflows)
.WithDeferredProvider()
```

### Default ChatOptions

```csharp
.WithDefaultOptions(new ChatOptions { Temperature = 0.7 })
```

### Secret resolution

```csharp
// Replace the entire resolver chain
.WithSecretResolver(myResolver)

// Add a resolver to the chain (evaluated in order)
.AddSecretResolver(myResolver)
```

---

## Identity & Instructions

```csharp
.WithName("SupportAgent")
.WithInstructions("You are a helpful support assistant.")
```

---

## Service Provider (Dependency Injection)

```csharp
.WithServiceProvider(services)   // IServiceProvider — required for DI-based toolkits and middleware
```

---

## Tools

```csharp
// Register a native C# toolkit by type (cannot be in JSON)
.WithToolkit<MyToolkit>()
.WithToolkit<MyToolkit>(contextMetadata)

// Register a toolkit with an existing instance
.WithToolkit<MyToolkit>(instance)
.WithToolkit<MyToolkit>(instance, contextMetadata)

// Register by Type object (dynamic / reflection)
.WithToolkit(typeof(MyToolkit))

// Aliases — identical behaviour to WithToolkit
.WithTools<MyToolkit>()
.WithTools<MyToolkit>(instance)
.WithTools(typeof(MyToolkit))

// Register state assemblies (for state-machine toolkits)
.WithStateAssembly<TMarker>()
.WithStateAssembly(assembly)
```

---

## Middleware

```csharp
// Register a single middleware
.WithMiddleware<MyMiddleware>()
.WithMiddleware(myMiddlewareInstance)

// Register multiple at once
.WithMiddlewares(middleware1, middleware2)

// Wrap the underlying IChatClient directly
.UseChatClientMiddleware((inner, sp) => new MyClientWrapper(inner))
```

### Error handling middleware

```csharp
// Retry transient tool/function errors
.WithFunctionRetry()
.WithFunctionRetry(cfg => cfg.MaxRetries = 5)

// Per-function timeout
.WithFunctionTimeout()
.WithFunctionTimeout(TimeSpan.FromSeconds(15))

// Format errors sent to the LLM
.WithErrorFormatting()
.WithErrorFormatting(includeDetailedErrors: true)

// Full error handling config in one call
.WithErrorHandling(cfg => { cfg.MaxRetries = 5; cfg.SingleFunctionTimeout = TimeSpan.FromSeconds(10); })
.WithErrorHandling(errorHandlingConfig)

// Circuit breaker — stops calling a tool after N identical consecutive calls
.WithCircuitBreaker(maxConsecutiveCalls: 3)
.WithCircuitBreaker(cb => cb.MaxConsecutiveCalls = 5)

// Stop the turn after N consecutive errors from any tool
.WithErrorTracking(maxConsecutiveErrors: 3)
.WithErrorTracking(et => { })

// Stop the turn after N total errors across the whole turn
.WithTotalErrorThreshold(maxTotalErrors: 10)
.WithTotalErrorThreshold(te => { })

// PII redaction
.WithPIIProtection()
.WithPIIProtection(pii => { })
```

---

## Observability

```csharp
// Structured logging
.WithLogging(LogLevel.Information)
.WithLogging(LogLevel.Debug, provider: myLoggerProvider)
.WithLogging(builder => builder.AddConsole())

// OpenTelemetry distributed tracing
.WithTracing()
.WithTracing("MyServiceName", sanitizerOptions)

// Both LLM-level and agent-level telemetry in one call
.WithTelemetry()
.WithTelemetry("MySourceName", enableSensitiveData: true)

// Custom event observer (fire-and-forget, not ordered)
.WithObserver(myObserver)       // implements IAgentEventObserver

// Synchronous ordered event handler
.WithEventHandler(myHandler)    // implements IAgentEventHandler
```

---

## Session Store

```csharp
// Attach any ISessionStore — auto-save after each turn enabled by default
.WithSessionStore(myStore)

// Control auto-save explicitly
.WithSessionStore(myStore, persistAfterTurn: false)

// Convenience: file-based JSON session store
.WithSessionStore("./sessions", persistAfterTurn: true)
```

---

## Collapsing (Toolkit Hierarchy)

```csharp
// Enable collapsing with default config
.WithToolCollapsing()

// Customise
.WithToolCollapsing(cfg =>
{
    cfg.NeverCollapse = new HashSet<string> { "FileToolkit" };
    cfg.MaxFunctionNamesInDescription = 5;
})

// Disable collapsing entirely
.WithoutToolCollapsing()
```

---

## History Reduction

```csharp
// Enable with full config
.WithHistoryReduction(cfg =>
{
    cfg.Enabled = true;
    cfg.Strategy = HistoryReductionStrategy.MessageCounting;
    cfg.TargetCount = 20;
})

// Shorthand: message-count based
.WithMessageCountingReduction(targetMessageCount: 20, threshold: 5)

// Shorthand: summarizing
.WithSummarizingReduction(targetMessageCount: 20, threshold: 5, customPrompt: null)

// Configure a separate summarizer provider
.WithSummarizerProvider("openai", "gpt-4o-mini", apiKey: "...")
.WithSummarizerProvider(cfg => { cfg.ProviderKey = "openai"; cfg.ModelName = "gpt-4o-mini"; })
```

---

## Caching

```csharp
.WithCaching()                                                      // Defaults (30 min TTL)
.WithCaching(TimeSpan.FromHours(1))                                 // Custom TTL
.WithCaching(TimeSpan.FromHours(1), cacheStatefulConversations: true)
```

The `IDistributedCache` implementation must be registered in the service provider.

---

## Reasoning

```csharp
// Set default reasoning for all calls from this agent
.WithReasoning(ReasoningEffort.High, ReasoningOutput.Summary)

// Keep reasoning tokens in conversation history
.WithPreserveReasoningInHistory(true)
```

---

## Agentic Loop

```csharp
// Max tool-calling iterations per turn
.WithMaxFunctionCallTurns(15)
// Alias
.WithMaxFunctionCalls(15)

// Extra turns when the user continues past the limit
.WithContinuationExtensionAmount(5)
```

---

## Content Store

```csharp
// Register a custom IContentStore
.WithContentStore(myStore)

// One-liner for default V3 content store setup
.UseDefaultContentStore()
.UseDefaultContentStore(myExistingStore)
```

---

## Configuration from IConfiguration / JSON

```csharp
// Load provider/model settings from Microsoft.Extensions.Configuration
.WithAPIConfiguration(configuration)
.WithAPIConfiguration("appsettings.json", optional: false, reloadOnChange: true)
```

---

## Dynamic ChatOptions

Apply a transform to `ChatOptions` on every request:

```csharp
.WithOptionsConfiguration(opts =>
{
    opts.StopSequences = ["DONE"];
})
```

---

## Validation

```csharp
// Enable async provider validation during BuildAsync() (makes a network call)
.WithValidation(enableAsync: true)
```

---

## Background Responses

For providers that support async/background execution:

```csharp
.WithBackgroundResponses(enabled: true)

.WithBackgroundResponses(cfg =>
{
    cfg.DefaultAllow = true;
    cfg.DefaultPollingInterval = TimeSpan.FromSeconds(5);
    cfg.AutoPollToCompletion = true;
})
```

---

## Build

```csharp
// Async (recommended)
var agent = await builder.BuildAsync();
var agent = await builder.BuildAsync(cancellationToken);
```

---

## See Also

- [Agent Config](Agent%20Config.md) — serializable config reference
- [Run Config](Run%20Config.md) — per-invocation overrides
- [Providers](Providers/00%20Providers%20Overview.md)
- [Error Handling](Error%20Handling.md)
- [History Reduction](History%20Reduction.md)
- [Caching](Caching.md)
- [Collapsing](Collapsing.md)
- [Observability](Observability.md)
- [Sandbox Config](Sandbox%20Config.md)

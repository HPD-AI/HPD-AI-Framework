# Agent Config

`AgentConfig` is the serializable data class that holds all build-time agent configuration. It can be defined in C# or loaded from a JSON file, and passed into `AgentBuilder` as the base configuration.

```csharp
var config = new AgentConfig
{
    Name = "SupportAgent",
    SystemInstructions = "You are a helpful support assistant.",
    Provider = new ProviderConfig
    {
        ProviderKey = "openai",
        ModelName = "gpt-4o",
        ApiKey = "sk-..."
    }
};

var agent = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .BuildAsync();
```

---

## Core Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | `string` | `"HPD-Agent"` | Agent display name |
| `SystemInstructions` | `string` | `"You are a helpful assistant."` | System prompt / persona |
| `MaxAgenticIterations` | `int` | `10` | Max tool-calling iterations per turn before a continuation is required |
| `ContinuationExtensionAmount` | `int` | `3` | Extra iterations granted when the user continues past the limit |
| `CoalesceDeltas` | `bool` | `false` | Merge streaming text/reasoning chunks into complete events instead of deltas |
| `PreserveReasoningInHistory` | `bool` | `false` | Include extended reasoning tokens in conversation history |
| `DefaultReasoning` | `ReasoningOptions?` | `null` | Default reasoning effort/output applied to all calls |

---

## Provider

```csharp
Provider = new ProviderConfig
{
    ProviderKey = "openai",        // "openai", "anthropic", "ollama", "azureai", etc.
    ModelName = "gpt-4o",
    ApiKey = "sk-...",             // Optional — resolved via ISecretResolver if null
    Endpoint = null,               // Custom endpoint (Azure OpenAI, self-hosted, etc.)
    CustomHeaders = new Dictionary<string, string>
    {
        ["X-Organization"] = "myorg"
    },
    ProviderOptionsJson = null,    // Provider-specific config as a JSON string
    AdditionalProperties = null    // Legacy key-value config (deprecated)
}
```

`DefaultChatOptions` on `ProviderConfig` is `[JsonIgnore]` — set it programmatically.

`ProviderOptionsJson` supports typed round-trip helpers:
```csharp
config.Provider.SetTypedProviderConfig(new OpenAIProviderConfig { ... });
var typedConfig = config.Provider.GetTypedProviderConfig<OpenAIProviderConfig>();
```

See [Providers](Providers/00%20Providers%20Overview.md) for per-provider configuration.

---

## Toolkits

Register toolkits by class name (string shorthand) or rich reference object:

```csharp
// String shorthand
Toolkits = ["CalculatorToolkit", "FileToolkit"]

// Rich reference — restrict which functions are exposed, or pass toolkit config
Toolkits =
[
    new ToolkitReference { Name = "FileToolkit", Functions = ["ReadFile", "WriteFile"] },
    new ToolkitReference { Name = "SearchToolkit", Config = mySearchConfig }
]
```

In JSON:
```json
{
    "Toolkits": [
        "CalculatorToolkit",
        { "Name": "FileToolkit", "Functions": ["ReadFile", "WriteFile"] }
    ]
}
```

Native C# toolkits (type references) must be registered via `.WithToolkit<T>()` on the builder.

---

## Middlewares

Register middleware by class name or rich reference:

```csharp
Middlewares = ["LoggingMiddleware", "RateLimitMiddleware"]
```

Middleware executes in the order listed. Native middleware types must be registered via `.WithMiddleware<T>()` on the builder.

---

## Nested Config Sections

Each section is an optional nullable config class. All default to `null` (framework uses built-in defaults).

| Property | Type | Purpose |
|----------|------|---------|
| `ErrorHandling` | `ErrorHandlingConfig?` | Retries, timeouts, error formatting |
| `HistoryReduction` | `HistoryReductionConfig?` | Conversation history summarization/trimming |
| `AgenticLoop` | `AgenticLoopConfig?` | Turn duration limits and parallel function caps |
| `Caching` | `CachingConfig?` | LLM response caching |
| `Collapsing` | `CollapsingConfig` | Toolkit hierarchical collapse/expand (default: `new CollapsingConfig { Enabled = true }`) |
| `Observability` | `ObservabilityConfig?` | Event sampling and circuit breaker |
| `BackgroundResponses` | `BackgroundResponsesConfig?` | Long-running / async provider support |
| `DocumentHandling` | `DocumentHandlingConfig?` | File attachment extraction settings |
| `ToolSelection` | `ToolSelectionConfig?` | Default tool calling mode |
| `Messages` | `AgentMessagesConfig` | Customizable system messages (default: `new AgentMessagesConfig()`) |
| `Audio` | `AudioConfig?` | TTS/STT/VAD settings |
| `Validation` | `ValidationConfig?` | Provider validation on build |
| `Mcp` | `McpConfig?` | Model Context Protocol configuration |

Each section has its own reference page — see the links at the bottom of this page.

### `AgenticLoopConfig`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxTurnDuration` | `TimeSpan?` | `5 minutes` | Max wall-clock time for a single turn |
| `MaxParallelFunctions` | `int?` | `null` | Max concurrent function executions (`null` = unlimited) |
| `TerminateOnUnknownCalls` | `bool` | `false` | Stop the turn if the model calls a function that doesn't exist |

### `ToolSelectionConfig`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ToolMode` | `string` | `"Auto"` | `"Auto"`, `"None"`, `"RequireAny"`, or `"RequireSpecific"` |
| `RequiredFunctionName` | `string?` | `null` | Function name to force-call when `ToolMode = "RequireSpecific"` |

### `AgentMessagesConfig`

Customise the messages the framework sends to the LLM for system events. All support placeholder substitution.

| Property | Placeholders | Default |
|----------|-------------|---------|
| `MaxIterationsReached` | `{maxIterations}` | `"Maximum iteration limit reached ({maxIterations} iterations)..."` |
| `CircuitBreakerTriggered` | `{toolName}`, `{count}` | `"Circuit breaker triggered: '{toolName}' called {count} times..."` |
| `MaxConsecutiveErrors` | `{maxErrors}` | `"Exceeded maximum consecutive errors ({maxErrors})..."` |
| `PermissionDeniedDefault` | — | `"Permission denied by user."` |

### `ValidationConfig`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableAsyncValidation` | `bool` | `false` | Make a network call to validate the API key during `BuildAsync()` |
| `TimeoutMs` | `int` | `3000` | Validation request timeout in milliseconds |
| `FailOnValidationError` | `bool` | `false` | Throw if validation fails (default: warn and continue) |

### `McpConfig`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ManifestPath` | `string` | `""` | Path to the MCP manifest file |
| `Options` | `object?` | `null` | MCP-specific options (stored as `object` to avoid circular dependencies) |

### `DocumentHandlingConfig`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DocumentTagFormat` | `string?` | `null` | Custom injection format string. `null` uses the built-in default (`[ATTACHED_DOCUMENT[{0}]]...`) |
| `MaxFileSizeBytes` | `long` | `10 MB` | Maximum file size to process |

---

## Non-Serializable Properties

These exist on `AgentConfig` but are marked `[JsonIgnore]`. Set them programmatically:

| Property | Type | Description |
|----------|------|-------------|
| `SessionStore` | `ISessionStore?` | Durable session persistence |
| `SessionStoreOptions` | `SessionStoreOptions?` | Auto-save timing |
| `ServerConfiguredTools` | `IList<AITool>?` | Tools known to the server but not in `ChatOptions` |
| `ConfigureOptions` | `Action<ChatOptions>?` | Dynamic `ChatOptions` transform per request |
| `ChatClientMiddleware` | `List<Func<IChatClient, IServiceProvider?, IChatClient>>?` | Runtime wrappers around the `IChatClient` |

---

## Building from a JSON File

```csharp
// One-liner: load, deserialize, and build
var agent = await AgentConfig.BuildFromFileAsync("agent-config.json");

// Or use the builder for runtime additions
var agent = await new AgentBuilder("agent-config.json")
    .WithServiceProvider(services)
    .WithToolkit<MyToolkit>()
    .BuildAsync();
```

---

## See Also

- [Agent Builder](Agent%20Builder.md) — fluent builder API reference
- [Run Config](Run%20Config.md) — per-invocation overrides
- [Error Handling](Error%20Handling.md) — `ErrorHandlingConfig`
- [History Reduction](History%20Reduction.md) — `HistoryReductionConfig`
- [Caching](Caching.md) — `CachingConfig`
- [Collapsing](Collapsing.md) — `CollapsingConfig`
- [Observability](Observability.md) — `ObservabilityConfig`
- [Session Store](Session%20Store.md)
- [Sandbox Config](Sandbox%20Config.md) — `SandboxConfig`
- [Providers](Providers/00%20Providers%20Overview.md)

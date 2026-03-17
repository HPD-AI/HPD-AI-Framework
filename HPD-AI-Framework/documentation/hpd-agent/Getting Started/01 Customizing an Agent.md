# Customizing an Agent
There are **three primary patterns** to customize an agent:

1. **Builder Pattern (Fluent API):** Configure agents programmatically using a fluent, chainable API in C# code.
2. **Config Pattern (Data Model):** Define agent configuration as data (e.g., JSON or C# objects), enabling serialization, persistence, and reuse.
3. **Builder + Config Pattern:** Define the agent configuration as data and then layer on the fluent `AgentBuilder` API.

Each method offers distinct advantages and trade-offs.

**This guide covers:**
- Builder Pattern (Fluent API) - Configure agents programmatically
- Config Pattern (Data Model) - Define configuration as serializable data
- Hybrid approaches combining both patterns

### Builder Pattern (Fluent API)

```csharp
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .WithInstructions("You are helpful...")
    .WithToolkit<MyToolkit>()
    .WithTelemetry()
    .BuildAsync();
```

### Config Pattern

You can define agent configuration either as a C# class or as a JSON file. Here's a side-by-side comparison:

<div style="display: flex; gap: 20px;">
<div style="flex: 1;">

**C# Config Class:**

```csharp
var config = new AgentConfig
{
    Name = "MyAgent",
    SystemInstructions = "You are helpful...",
    Provider = new ProviderConfig
    {
        ProviderKey = "openai",
        ModelName = "gpt-4o",
        ApiKey = "sk-..."
    },
    MaxAgenticIterations = 10,
    Toolkits = new List<ToolkitReference>
    {
        "CalculatorToolkit",
        "FileToolkit"
    }
};

// Build directly from config
var agent = await config.BuildAsync();
```

</div>
<div style="flex: 1;">

**JSON Config File (agent-config.json):**

```json
{
    "Name": "MyAgent",
    "SystemInstructions": "You are helpful...",
    "Provider": {
        "ProviderKey": "openai",
        "ModelName": "gpt-4o",
        "ApiKey": "sk-..."
    },
    "MaxAgenticIterations": 10,
    "Toolkits": ["CalculatorToolkit", "FileToolkit"]
}
```
```csharp
// Load, deserialize, and build in one call
var agent = await AgentConfig.BuildFromFileAsync("agent-config.json");

// Sync version if needed
var agent = AgentConfig.BuildFromFile("agent-config.json");
```

</div>
</div>

### Builder + Config Pattern (Recommended Pattern)

This pattern combines the best of both worlds: define your agent configuration as data (for persistence and reuse), then layer on the fluent API for runtime customization.

<div style="display: flex; gap: 20px;">
<div style="flex: 1;">

**C# Config Class + Builder:**

```csharp
// Start with a config object
var config = new AgentConfig
{
    Name = "MyAgent",
    SystemInstructions = "You are helpful.",
    Provider = new ProviderConfig
    {
        ProviderKey = "openai",
        ModelName = "gpt-4o",
        ApiKey = "sk-..."
    },
    MaxAgenticIterations = 10
};

// Layer builder methods on top for runtime customization
var agent = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .WithLogging()
    .WithTelemetry()
    .WithToolkit<MyToolkit>()
    .WithMiddleware<MyCustomMiddleware>()
    .BuildAsync();
```

</div>
<div style="flex: 1;">

**JSON Config File + Builder:**

```json
{
    "Name": "MyAgent",
    "SystemInstructions": "You are helpful.",
    "Provider": {
        "ProviderKey": "openai",
        "ModelName": "gpt-4o",
        "ApiKey": "sk-..."
    },
    "MaxAgenticIterations": 10
}
```

**C# Usage:**
```csharp
// Load config from JSON
var agent = await new AgentBuilder("agent-config.json")
    .WithServiceProvider(services)
    .WithLogging()
    .WithTelemetry()
    .WithToolkit<MyToolkit>()
    .WithMiddleware<MyCustomMiddleware>()
    .BuildAsync();
```

</div>
</div>

### Why is the Builder + Config Pattern recommended for production?

The Builder + Config Pattern combines the strengths of both approaches while avoiding their weaknesses:

**Against Pure Builder Pattern - Builder Methods Get Messy:**

The core issue: with many configurations, the builder gets very long and repetitive.

1. **Too many chained calls** - Imagine 20+ `.With()` calls:
```csharp
// Pure Builder Pattern - becomes a wall of code!
var agent1 = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .WithInstructions("You are helpful Math assistant")
    .WithServiceProvider(services)
    .WithLogging()
    .WithTelemetry()
    .WithCaching(TimeSpan.FromHours(1))
    .WithMaxFunctionCallTurns(10)
    .WithContinuationExtensionAmount(3)
    .WithSessionStore("./sessions", persistAfterTurn: true)
    .WithMiddleware<MyCustomMiddleware>()
    .WithMiddleware<ErrorHandlingMiddleware>()
    .WithMiddleware<HistoryReductionMiddleware>()
    .WithToolkit<Toolkit1>()
    .WithToolkit<Toolkit2>()
    .WithToolkit<Toolkit3>()
    // ... 20+ more configuration calls
    .BuildAsync();
```

2. **Configuration duplication across multiple agents** - You have to repeat this everywhere:
```csharp
// If you have 10 agents with common config, all this is duplicated 10 times!
var agent2 = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")  // Repeated!
    .WithInstructions("You are helpful finance assistant")  // Repeated!
    .WithServiceProvider(services)  // Repeated!
    .WithLogging()  // Repeated!
    .WithTelemetry()  // Repeated!
    .WithCaching(TimeSpan.FromHours(1))  // Repeated!
    .WithMaxFunctionCallTurns(10)  // Repeated!
    .WithContinuationExtensionAmount(3)  // Repeated!
    .WithSessionStore("./sessions", persistAfterTurn: true)  // Repeated!
    .WithMiddleware<MyCustomMiddleware>()
    .WithMiddleware<ErrorHandlingMiddleware>()  
    .WithMiddleware<HistoryReductionMiddleware>()  
    .WithToolkit<Toolkit2>()
    .BuildAsync();
```

**With Builder + Config Pattern:**
- Configuration is defined once as data (DRY principle)
- Reusable across multiple agent instances
- Native C# tools registered via the fluent API with full type safety
- Clean separation: data configuration + runtime customization
- Easy to version control configuration separately from code
- Perfect for microservices and multi-agent systems

Example of clean solution:

<div style="display: flex; gap: 20px;">
<div style="flex: 1;">

**C# Config Class + Builder:**

```csharp
// Define config once as a C# class
var config = new AgentConfig
{
    Name = "MyAgent",
    SystemInstructions = "You are helpful.",
    Provider = new ProviderConfig
    {
        ProviderKey = "openai",
        ModelName = "gpt-4o",
        ApiKey = "sk-..."
    },
    MaxAgenticIterations = 10,
    Toolkits = new List<ToolkitReference>
    {
        "CalculatorToolkit",
        "FileToolkit"
    },
    Middlewares = new List<MiddlewareReference>
    {
        "LoggingMiddleware"
    },
    Caching = new CachingConfig
    {
        Enabled = true,
        CacheExpiration = TimeSpan.FromMinutes(30)
    },
    ErrorHandling = new ErrorHandlingConfig
    {
        MaxRetries = 3,
        SingleFunctionTimeout = TimeSpan.FromSeconds(30)
    }
};

// Reuse across multiple agents with different overrides
var agent1 = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .BuildAsync();

var agent2 = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .WithToolkit<AdditionalTools>()  // Extend with more tools
    .BuildAsync();
```

</div>
<div style="flex: 1;">

**JSON Config File + Builder:**

```json
{
    "Name": "MyAgent",
    "SystemInstructions": "You are helpful.",
    "Provider": {
        "ProviderKey": "openai",
        "ModelName": "gpt-4o",
        "ApiKey": "sk-..."
    },
    "MaxAgenticIterations": 10,
    "Toolkits": ["CalculatorToolkit", "FileToolkit"],
    "Middlewares": ["LoggingMiddleware"],
    "Caching": {
        "Enabled": true,
        "CacheExpiration": "00:30:00"
    },
    "ErrorHandling": {
        "MaxRetries": 3,
        "SingleFunctionTimeout": "00:00:30"
    },
    "AgenticLoop": {
        "MaxTurnDuration": "00:05:00"
    }
}
```

**C# Usage:**
```csharp
// Reuse across multiple agents with different tool overrides
var agent1 = await new AgentBuilder("agent-config.json")
    .WithServiceProvider(services)
    .BuildAsync();

var agent2 = await new AgentBuilder("agent-config.json")
    .WithServiceProvider(services)
    .WithToolkit<AdditionalTools>()  // Extend with more tools
    .BuildAsync();
```

</div>
</div>

**Key insight:** All configuration (toolkits, middlewares, caching, error handling, timeouts) is centralized once. The builder pattern lets you extend or override as needed. No repetition—just load and customize.

---

## AgentBuilder Constructor Reference

`AgentBuilder` has three constructors:

```csharp
// 1. Default — blank config, auto-discovers toolkits in the calling assembly
new AgentBuilder()

// 2. From a config object — starts with all values from the AgentConfig
new AgentBuilder(AgentConfig config)

// 3. From a JSON file — loads and deserializes the file, then applies it as the base config
new AgentBuilder(string configPath)
```

**`new AgentBuilder(AgentConfig config)`** is the recommended constructor for production. It takes a fully-populated `AgentConfig` as the starting point and lets you layer runtime-only concerns on top via builder methods:

```csharp
var config = new AgentConfig
{
    Name = "SupportAgent",
    SystemInstructions = "You are a support assistant.",
    Provider = new ProviderConfig { ProviderKey = "openai", ModelName = "gpt-4o" },
    MaxAgenticIterations = 15,
    Toolkits = ["KnowledgeToolkit"],
    Middlewares = ["LoggingMiddleware"]
};

var agent = await new AgentBuilder(config)
    // Runtime-only additions — these cannot be serialized to JSON:
    .WithServiceProvider(services)    // DI container
    .WithToolkit<MyCompiledTool>()    // Native C# toolkit (type reference)
    .WithMiddleware<CustomMiddleware>() // Native middleware
    .BuildAsync();
```

**Builder methods take precedence over config.** If `config.Provider` is set and you also call `.WithProvider(...)`, the builder value wins.

### AgentConfig Properties

`AgentConfig` is a serializable data class. Key properties:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | `string` | `"HPD-Agent"` | Agent name |
| `SystemInstructions` | `string` | `"You are a helpful assistant."` | System prompt |
| `MaxAgenticIterations` | `int` | `10` | Maximum tool-calling iterations per turn |
| `ContinuationExtensionAmount` | `int` | `3` | Extra iterations granted on continuation |
| `Provider` | `ProviderConfig?` | `null` | LLM provider and model |
| `Toolkits` | `List<ToolkitReference>` | `[]` | Toolkits to register |
| `Middlewares` | `List<MiddlewareReference>` | `[]` | Middleware pipeline |
| `Caching` | `CachingConfig?` | `null` | Prompt caching settings |
| `ErrorHandling` | `ErrorHandlingConfig?` | `null` | Retry and timeout settings |
| `HistoryReduction` | `HistoryReductionConfig?` | `null` | Conversation history summarization |
| `AgenticLoop` | `AgenticLoopConfig?` | `null` | Loop timeout and control settings |
| `Collapsing` | `CollapsingConfig` | enabled | Toolkit collapse/expand behaviour |
| `Observability` | `ObservabilityConfig?` | `null` | Tracing and metrics |
| `PreserveReasoningTokens` | `bool` | `false` | Keep extended reasoning in history |
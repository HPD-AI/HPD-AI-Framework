# Run Config

> Per-invocation customization without rebuilding the agent

`AgentRunConfig` is the optional last parameter on every `RunAsync` call. It lets you adjust provider, model, temperature, system instructions, tool behaviour, and more on a per-request basis — without touching `AgentConfig` or rebuilding the agent.

```csharp
var options = new AgentRunConfig
{
    Chat = new ChatRunConfig { Temperature = 0.2 },
    AdditionalSystemInstructions = "Be concise. Respond in bullet points."
};

await foreach (var evt in agent.RunAsync("Summarize this", branch, options)) { }
```

---

## AgentRunConfig Properties

### Provider & Model

Switch the LLM for a single run without rebuilding:

```csharp
// Switch provider and model
var options = new AgentRunConfig
{
    ProviderKey = "anthropic",
    ModelId = "claude-opus-4-6"
};

// Use a specific API key for this request (multi-tenant scenarios)
var options = new AgentRunConfig
{
    ProviderKey = "openai",
    ModelId = "gpt-4o",
    ApiKey = user.OpenAiApiKey
};

// Custom endpoint (self-hosted, Azure OpenAI, etc.)
var options = new AgentRunConfig
{
    ProviderKey = "openai",
    ProviderEndpoint = "https://my-azure-instance.openai.azure.com/",
    ApiKey = azureKey
};
```

Priority: `OverrideChatClient` > `ProviderKey`/`ModelId` > agent's default client.

| Property | Type | Description |
|----------|------|-------------|
| `ProviderKey` | `string?` | Provider to switch to (`"openai"`, `"anthropic"`, `"ollama"`, …) |
| `ModelId` | `string?` | Model ID for the switched provider |
| `ApiKey` | `string?` | API key override |
| `ProviderEndpoint` | `string?` | Endpoint URL override |
| `CustomHeaders` | `Dictionary<string, string>?` | Extra HTTP headers for provider requests |
| `OverrideChatClient` | `IChatClient?` | Bypass registry entirely — use this client directly (not JSON-serializable) |

---

### Chat Parameters

Adjust sampling parameters without touching the agent config:

```csharp
var options = new AgentRunConfig
{
    Chat = new ChatRunConfig
    {
        Temperature = 0.0,        // Deterministic output
        MaxOutputTokens = 512,    // Limit response length
        Reasoning = new ReasoningOptions
        {
            Effort = ReasoningEffort.High,
            Output = ReasoningOutput.Summary
        }
    }
};
```

`ChatRunConfig` properties (all nullable — `null` inherits from agent config):

| Property | Type | Description |
|----------|------|-------------|
| `Temperature` | `double?` | 0.0–2.0. Higher = more creative |
| `TopP` | `double?` | 0.0–1.0. Nucleus sampling |
| `TopK` | `int?` | Number of most-probable tokens considered |
| `MaxOutputTokens` | `int?` | Maximum tokens in the response |
| `FrequencyPenalty` | `double?` | −2.0–2.0. Reduces repetition |
| `PresencePenalty` | `double?` | −2.0–2.0. Encourages new topics |
| `StopSequences` | `IReadOnlyList<string>?` | Sequences that stop generation |
| `ModelId` | `string?` | Model override scoped to chat parameters (alternative to top-level `ModelId`) |
| `AdditionalProperties` | `Dictionary<string, object>?` | Provider-specific extra parameters passed through to `ChatOptions` |
| `ResponseFormat` | `ChatResponseFormat?` | Structured output format (not JSON-serializable) |
| `Reasoning` | `ReasoningOptions?` | Reasoning effort and output mode (see below) |

### Reasoning

Control extended thinking (Claude, o-series, DeepSeek-R1):

```csharp
Chat = new ChatRunConfig
{
    Reasoning = new ReasoningOptions
    {
        Effort = ReasoningEffort.High,    // None / Low / Medium / High / ExtraHigh
        Output = ReasoningOutput.Summary  // None / Summary / Full
    }
}
```

`Effort` controls how much computational budget the model spends on reasoning. `Output` controls whether reasoning content appears in the event stream (`ReasoningDeltaEvent`).

---

### System Instructions

Replace or augment the agent's system prompt for one run:

```csharp
// Append to existing instructions (most common)
var options = new AgentRunConfig
{
    AdditionalSystemInstructions = "For this request: respond only in French."
};

// Completely replace the system prompt
var options = new AgentRunConfig
{
    SystemInstructions = "You are a strict JSON-only responder. Output nothing but valid JSON."
};
```

If both are set, `SystemInstructions` replaces the base, then `AdditionalSystemInstructions` appends to that.

| Property | Description |
|----------|-------------|
| `SystemInstructions` | Completely replaces the configured system instructions |
| `AdditionalSystemInstructions` | Appends to the resolved instructions |

---

### Execution Control

```csharp
var options = new AgentRunConfig
{
    RunTimeout = TimeSpan.FromSeconds(10),  // Abort if not done in 10s
    SkipTools = true,                        // Dry run — plan but don't execute tools
    CoalesceDeltas = true,                   // Batch streaming chunks into one event
    UseCache = false                         // Force fresh response, skip cache
};
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RunTimeout` | `TimeSpan?` | config | Timeout for this entire run |
| `SkipTools` | `bool` | `false` | Plan tool calls but don't execute them |
| `CoalesceDeltas` | `bool?` | config | Merge streaming text/reasoning chunks into one event |
| `UseCache` | `bool?` | config | `false` = skip cache; `true` = force cache |

---

### Context & Permissions

Inject per-request data for middleware and tools, or override permissions:

```csharp
var options = new AgentRunConfig
{
    // Available to middleware via context.Properties["userId"]
    ContextOverrides = new Dictionary<string, object>
    {
        ["userId"] = user.Id,
        ["tenantId"] = user.TenantId,
        ["tier"] = "premium"
    },

    // Runtime context for tools with [AIFunction<TMetadata>] (not JSON-serializable)
    ContextInstances = new Dictionary<string, IToolMetadata>
    {
        ["SearchTools"] = new SearchMetadata { ProviderName = user.PreferredSearch }
    },

    // Override permissions for this run only
    PermissionOverrides = new Dictionary<string, bool>
    {
        ["delete_files"] = false,         // Deny even if normally allowed
        ["external_api_calls"] = true     // Allow even if normally denied
    }
};
```

`ContextInstances` is the runtime injection point for per-invocation tool metadata. See [02.1.4 Tool Dynamic Metadata](../Tools/02.1.4%20Tool%20Dynamic%20Metadata.md) for how this connects to `[AIFunction<TMetadata>]`.

---

### Tools

Inject extra tools or change tool mode for a single run:

```csharp
// Add a handoff tool dynamically (multi-agent patterns)
var handoff = AIFunctionFactory.Create(
    () => "routed",
    "handoff_to_billing",
    "Transfer this conversation to the billing agent");

var options = new AgentRunConfig
{
    AdditionalTools = [handoff]
};

// Force the model to call a specific tool
var options = new AgentRunConfig
{
    ToolModeOverride = ChatToolMode.RequireTool("handoff_to_billing")
};
```

| Property | Type | Description |
|----------|------|-------------|
| `AdditionalTools` | `IReadOnlyList<AIFunction>?` | Extra tools merged in for this run only (not JSON-serializable) |
| `ToolModeOverride` | `ChatToolMode?` | Override tool calling mode (`Auto`, `RequireAny`, `RequireTool(name)`) (not JSON-serializable) |
| `RuntimeMiddleware` | `IReadOnlyList<IAgentMiddleware>?` | Extra middleware injected only for this run (not JSON-serializable) |

---

### Attachments

Send binary content alongside the message without building a `ChatMessage` list manually:

```csharp
var options = new AgentRunConfig
{
    UserMessage = "What does this document say?",
    Attachments = [await DocumentContent.FromFileAsync("report.pdf")]
};

await foreach (var evt in agent.RunAsync(options, branch)) { }
```

Attachments can be used without a `UserMessage` — middleware handles content-only inputs (e.g., `AudioPipelineMiddleware` transcribes audio-only input into the message).

| Property | Type | Description |
|----------|------|-------------|
| `UserMessage` | `string?` | Text message for this run (combined with Attachments) |
| `Attachments` | `IReadOnlyList<DataContent>?` | Images, audio, documents, video (not JSON-serializable) |

---

### History Reduction

Override the history reduction behaviour for one turn:

```csharp
// Force reduction now (context switch, expensive operation coming up)
var options = new AgentRunConfig { TriggerHistoryReduction = true };

// Skip reduction (important decision — need full context)
var options = new AgentRunConfig { SkipHistoryReduction = true };

// Override the reduction mode for this turn only
var options = new AgentRunConfig
{
    HistoryReductionBehaviorOverride = HistoryReductionBehavior.CircuitBreaker
};
```

`SkipHistoryReduction` takes precedence over `TriggerHistoryReduction` if both are set.

---

### Structured Output

Force the model to respond in a specific JSON schema:

```csharp
var options = new AgentRunConfig
{
    StructuredOutput = new StructuredOutputOptions { Schema = myJsonSchema }
};
```

`StructuredOutput` is not JSON-serializable — set it in C# only.

---

### Conversation & Client

| Property | Type | Description |
|----------|------|-------------|
| `ConversationIdOverride` | `string?` | Override the conversation ID for this run (useful for caching and stateful routing) |
| `ClientToolInput` | `AgentClientInput?` | Input from a client-side tool invocation (used in client tool flows) |

---

### Background Responses

For providers that support async/background execution:

```csharp
// Allow background mode — may return a ContinuationToken immediately
var options = new AgentRunConfig { AllowBackgroundResponses = true };

// Configure polling behaviour
var options = new AgentRunConfig
{
    AllowBackgroundResponses = true,
    BackgroundPollingInterval = TimeSpan.FromSeconds(5),
    BackgroundTimeout = TimeSpan.FromMinutes(2)
};

// Resume or poll a previous background operation
var options = new AgentRunConfig
{
    ContinuationToken = previousResponse.ContinuationToken
};
```

| Property | Type | Description |
|----------|------|-------------|
| `AllowBackgroundResponses` | `bool?` | Allow this run to return a continuation token and complete in the background |
| `BackgroundPollingInterval` | `TimeSpan?` | Hint for how often to poll for completion |
| `BackgroundTimeout` | `TimeSpan?` | Maximum time to wait for a background response before giving up |
| `ContinuationToken` | `ResponseContinuationToken?` | Resume a previous background operation (not JSON-serializable) |

---

## JSON-Serializable Subset

`AgentRunConfig` is designed to be partially JSON-serializable. Properties marked `[JsonIgnore]` require direct C# usage and cannot be sent over the wire. The `StreamRunConfigDto` type is the wire format for web API calls and only exposes the serializable subset:

| Serializable | Not serializable (`[JsonIgnore]`) |
|---|---|
| `Chat`, `ProviderKey`, `ModelId`, `ApiKey`, `ProviderEndpoint` | `OverrideChatClient` |
| `SystemInstructions`, `AdditionalSystemInstructions` | `RuntimeMiddleware` |
| `ContextOverrides`, `PermissionOverrides` | `ContextInstances` |
| `RunTimeout`, `SkipTools`, `CoalesceDeltas`, `UseCache` | `AdditionalTools`, `ToolModeOverride` |
| `TriggerHistoryReduction`, `SkipHistoryReduction`, `HistoryReductionBehaviorOverride` | `Attachments`, `CustomStreamCallback` |
| `AllowBackgroundResponses`, `BackgroundPollingInterval`, `BackgroundTimeout` | `ContinuationToken` |
| `ConversationIdOverride`, `ClientToolInput` | `StructuredOutput`, `Chat.ResponseFormat` |

In web apps, the TypeScript client sends `StreamRunConfigDto` fields as part of the stream request body. C#-only properties must be injected server-side via `ConfigureAgent` or middleware.

---

## See Also

- [01 Customizing an Agent](../Getting%20Started/01%20Customizing%20an%20Agent.md) — build-time configuration (`AgentConfig`)
- [02 Multi-Turn Conversations](../Getting%20Started/02%20Multi-Turn%20Conversations.md) — `RunAsync` signatures
- [02.1.4 Tool Dynamic Metadata](../Tools/02.1.4%20Tool%20Dynamic%20Metadata.md) — `ContextInstances` and per-invocation metadata

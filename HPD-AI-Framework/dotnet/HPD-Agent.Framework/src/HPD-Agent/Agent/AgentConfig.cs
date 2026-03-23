using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using HPD.Agent;

using System.Collections.Immutable;

namespace HPD.Agent;

/// A data-centric class that holds all the serializable configuration
/// for creating a new agent.
/// </summary>
public class AgentConfig
{
    /// <summary>
    /// Global configuration instance used by source-generated code.
    /// Set by AgentBuilder during agent construction.
    /// </summary>
    public static AgentConfig? GlobalConfig { get; set; }

    public string Name { get; set; } = "HPD-Agent";
    public string SystemInstructions { get; set; } = "You are a helpful assistant.";
    
    /// <summary>
    /// Maximum number of turns the agent can take to call functions before requiring continuation permission.
    /// Each turn allows the LLM to analyze previous results and decide whether to call more functions or provide a final response.
    /// </summary>
    public int MaxAgenticIterations { get; set; } = 10;
    
    /// <summary>
    /// How many additional turns to allow when user chooses to continue beyond the limit.
    /// This includes extra iterations for the LLM to complete its task and generate a final response.
    /// </summary>
    public int ContinuationExtensionAmount { get; set; } = 3;

    /// <summary>
    /// Configuration for the AI provider (e.g., OpenAI, Ollama).
    /// </summary>
    public ProviderConfig? Provider { get; set; }

    /// <summary>
    /// Configuration for provider validation behavior during agent building.
    /// </summary>
    public ValidationConfig? Validation { get; set; }

    /// <summary>
    /// Configuration for the Model Context Protocol (MCP).
    /// </summary>
    public McpConfig? Mcp { get; set; }

    /// <summary>
    /// Configuration for error handling behavior.
    /// </summary>
    public ErrorHandlingConfig? ErrorHandling { get; set; }

    /// <summary>
    /// Configuration for document handling behavior.
    /// </summary>
    public DocumentHandlingConfig? DocumentHandling { get; set; }

    /// <summary>
    /// Configuration for conversation history reduction to manage context window size.
    /// </summary>
    public HistoryReductionConfig? HistoryReduction { get; set; }

    /// <summary>
    /// Configuration for agentic loop safety controls (timeouts, circuit breakers).
    /// </summary>
    public AgenticLoopConfig? AgenticLoop { get; set; }

    /// <summary>
    /// Configuration for agent system messages (termination messages, error messages, etc.).
    /// Allows customization for internationalization, branding, or context-specific needs.
    /// </summary>
    public AgentMessagesConfig Messages { get; set; } = new();

    /// <summary>
    /// Configuration for tool selection behavior (how the LLM chooses which tools to use).
    /// </summary>
    public ToolSelectionConfig? ToolSelection { get; set; }

    /// <summary>
    /// Configuration for Collapsing - hierarchical organization of functions to reduce token usage.
    /// When enabled, functions are hidden behind container functions, reducing initial tool list by up to 87.5%.
    /// Default: Collapsing enabled.
    /// </summary>
    public CollapsingConfig Collapsing { get; set; } = new CollapsingConfig { Enabled = true };

    /// <summary>
    /// Toolkits to include. Supports both simple string names and rich references.
    /// Resolved via source-generated registry at Build() time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Simple syntax:</b>
    /// <code>
    /// { "toolkits": ["MathToolkit", "SearchToolkit"] }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Rich syntax:</b>
    /// <code>
    /// {
    ///   "toolkits": [
    ///     "MathToolkit",
    ///     { "name": "FileToolkit", "functions": ["ReadFile", "WriteFile"] },
    ///     { "name": "ApiToolkit", "config": { "apiKey": "${API_KEY}" } }
    ///   ]
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public List<ToolkitReference> Toolkits { get; set; } = new();

    /// <summary>
    /// Middleware names to include (in order). Supports both simple string names and rich references.
    /// Resolved via source-generated registry at Build() time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Middlewares execute in the order listed. This list contains user-defined middleware only.
    /// Internal middleware (Container, Retry, Timeout, etc.) are controlled by their respective config sections.
    /// </para>
    /// <para>
    /// <b>Simple syntax:</b>
    /// <code>
    /// { "middlewares": ["LoggingMiddleware", "RetryMiddleware"] }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Rich syntax:</b>
    /// <code>
    /// {
    ///   "middlewares": [
    ///     "LoggingMiddleware",
    ///     { "name": "RateLimitMiddleware", "config": { "requestsPerMinute": 60 } }
    ///   ]
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public List<MiddlewareReference> Middlewares { get; set; } = new();

    /// <summary>
    /// Internal: Set of explicitly registered Toolkit names (for Collapsing manager).
    /// This is set by the builder and used to distinguish explicit vs implicit Toolkit registration.
    /// </summary>
    [JsonIgnore]
    public ImmutableHashSet<string> explicitlyRegisteredToolkits { get; set; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Configuration for distributed caching of LLM responses.
    /// Dramatically reduces latency and cost for repeated queries.
    /// Requires IDistributedCache to be registered via AgentBuilder.WithServiceProvider().
    /// </summary>
    public CachingConfig? Caching { get; set; }

    /// <summary>
    /// Configuration for event observer sampling and performance optimization.
    /// Controls circuit breaker thresholds and event sampling rates for high-volume events.
    /// </summary>
    public ObservabilityConfig? Observability { get; set; }

    /// <summary>
    /// Configuration for background responses behavior.
    /// Enables long-running LLM operations to return immediately with polling tokens.
    /// </summary>
    public BackgroundResponsesConfig? BackgroundResponses { get; set; }

    /// <summary>
    /// Configuration for audio providers (TTS/STT/VAD).
    /// Enables voice interaction capabilities for the agent.
    /// </summary>
    public AudioConfig? Audio { get; set; }

    /// <summary>
    /// Whether to preserve reasoning tokens (from models like o1, DeepSeek-R1) in conversation history.
    /// Default: false (reasoning is shown during streaming but excluded from history to save tokens).
    /// When true, reasoning content is included in history and available in future context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Trade-offs:</b>
    /// - false (default): Lower cost, smaller context - reasoning shown in UI but not in future prompts
    /// - true: Higher cost, larger context - full reasoning preserved for complex multi-turn scenarios
    /// </para>
    /// <para>
    /// <b>When to enable:</b>
    /// - Research/debugging where full reasoning trace is needed
    /// - Complex multi-turn reasoning where previous thoughts inform future responses
    /// - Scenarios where preserving the model's thought process is critical
    /// </para>
    /// <para>
    /// <b>Cost implications:</b>
    /// Reasoning models can produce significant reasoning content (often 10x-50x the output length).
    /// Including this in history means paying for those tokens on every subsequent request.
    /// </para>
    /// </remarks>
    public bool PreserveReasoningInHistory { get; set; } = false;

    /// <summary>
    /// Default reasoning options applied to every LLM call made by this agent.
    /// Controls reasoning effort and output exposure for the underlying model.
    /// Can be overridden per-run via <see cref="AgentRunConfig.Chat"/>'s <see cref="ChatRunConfig.Reasoning"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Effort:</b> None, Low, Medium, High, ExtraHigh — how much thinking the model applies.
    /// </para>
    /// <para>
    /// <b>Output:</b> None, Summary, Full — whether reasoning content is returned in the response
    /// and therefore visible during streaming and (if <see cref="PreserveReasoningInHistory"/> is true) persisted.
    /// </para>
    /// <para>
    /// <b>Provider support:</b> Anthropic (extended thinking via ThinkingBudgetTokens),
    /// OpenAI o-series (native), DeepSeek-R1, Google Gemini thinking models.
    /// Providers that do not support reasoning ignore this setting.
    /// </para>
    /// </remarks>
    public ReasoningOptions? DefaultReasoning { get; set; }

    /// <summary>
    /// When true, coalesces streaming text and reasoning deltas into single complete events.
    /// - Without: Emits multiple TextDeltaEvent for each chunk ("Hello", " ", "world")
    /// - With: Emits single TextDeltaEvent with complete text ("Hello world")
    /// Reduces event count and simplifies processing at the cost of increased latency.
    /// Can be overridden per-run via AgentRunConfig.CoalesceDeltas.
    /// Default: false (immediate streaming for progressive rendering)
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Trade-offs:</b>
    /// - false (default): Lower latency, progressive rendering - events stream as they arrive
    /// - true: Higher latency, complete events - all deltas buffered until completion
    /// </para>
    /// <para>
    /// <b>When to enable:</b>
    /// - UIs that prefer complete responses over progressive rendering
    /// - Testing scenarios where complete text is easier to assert
    /// - Batch processing where event count matters more than latency
    /// - Logging/analytics where complete messages are cleaner
    /// </para>
    /// <para>
    /// <b>What gets coalesced:</b>
    /// - Text deltas: Multiple TextDeltaEvent → Single complete TextDeltaEvent
    /// - Reasoning deltas: Multiple ReasoningDeltaEvent → Single complete ReasoningDeltaEvent
    /// - Tool calls, message boundaries, and other events are unaffected
    /// </para>
    /// </remarks>
    public bool CoalesceDeltas { get; set; } = false;

    /// <summary>
    /// Optional session store for durable execution and crash recovery.
    /// Use InMemorySessionStore for development/testing or JsonSessionStore for production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Example - Auto-save mode:</b>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithSessionStore(new JsonSessionStore("./sessions"), autoSave: true)
    ///     .Build();
    ///
    /// // One line - load, run, save all handled
    /// await agent.RunAsync("Hello", "session-123");
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public ISessionStore? SessionStore { get; set; }

    /// <summary>
    /// Agent store used to resolve <see cref="StoredAgent"/> definitions at runtime.
    /// Required when using sub-agents via <c>StoredAgentId</c>.
    /// </summary>
    [JsonIgnore]
    public IAgentStore? AgentStore { get; set; }

    /// <summary>
    /// Options for session persistence behavior.
    /// Controls auto-save, checkpoint frequency, and retention policy.
    /// </summary>
    [JsonIgnore]
    public SessionStoreOptions? SessionStoreOptions { get; set; }

    // Branching config removed - branching is now an application-level concern

    /// <summary>
    /// Tools that the agent can invoke but are NOT sent to the LLM in each request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some AI services (e.g., OpenAI Assistants, Anthropic with pre-configured tools) allow you to
    /// configure functions server-side that persist across requests. When the LLM calls these functions,
    /// your agent needs to be able to execute them even though they weren't in <see cref="ChatOptions.Tools"/>.
    /// </para>
    /// <para>
    /// <b>Use Cases:</b>
    /// <list type="bullet">
    /// <item>OpenAI Assistants with pre-configured tools</item>
    /// <item>Azure AI Function Apps registered with the service</item>
    /// <item>Anthropic accounts with account-level tool configurations</item>
    /// <item>Testing scenarios where you want to hide tools from the LLM but still handle calls</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Priority:</b> If a function exists in both <see cref="ChatOptions.Tools"/> and ServerConfiguredTools,
    /// the one in <see cref="ChatOptions.Tools"/> takes precedence (allows per-request overrides).
    /// </para>
    /// <para>
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var agent = new Agent(new AgentConfig
    /// {
    ///     ServerConfiguredTools = [get_weather_function, search_web_function]
    /// });
    ///
    /// // Request doesn't include tools (they're server-configured)
    /// var response = await agent.GetResponseAsync(messages, new ChatOptions());
    ///
    /// // LLM calls "get_weather" (server knows about it)
    /// // Agent finds it in ServerConfiguredTools and executes it
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore] // Don't serialize AIFunction instances
    public IList<AITool>? ServerConfiguredTools { get; set; }

    /// <summary>
    /// Optional callback to configure or transform ChatOptions before each LLM call.
    /// This allows dynamic runtime configuration without middleware.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This callback is invoked before every LLM request, allowing you to:
    /// - Dynamically adjust temperature, top_p, etc. based on runtime conditions
    /// - Add request-specific metadata or tracking
    /// - Enforce constraints (e.g., cap max tokens)
    /// - Implement custom option transformation logic
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var config = new AgentConfig
    /// {
    ///     ConfigureOptions = opts =>
    ///     {
    ///         // Cap temperature at 0.8
    ///         opts.Temperature = Math.Min(opts.Temperature ?? 1.0f, 0.8f);
    ///
    ///         // Add request ID for tracking
    ///         opts.AdditionalProperties ??= new();
    ///         opts.AdditionalProperties["request_id"] = Guid.NewGuid().ToString();
    ///     }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore] // Don't serialize callbacks
    public Action<ChatOptions>? ConfigureOptions { get; set; }

    /// <summary>
    /// Optional middleware to wrap the IChatClient for custom processing.
    /// Middleware is applied dynamically on each request, allowing runtime provider switching to work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike traditional middleware that wraps the client at build time, this middleware is applied
    /// on every request. This means:
    /// - Runtime provider switching still works (new provider gets wrapped automatically)
    /// - No performance overhead when middleware list is null/empty
    /// - Middleware can be added/removed at runtime if needed
    /// </para>
    /// <para>
    /// <b>Use Cases:</b>
    /// - Custom rate limiting
    /// - Cost tracking and budgets
    /// - Request/response logging
    /// - Response caching
    /// - Content filtering
    /// - Retry policies
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var config = new AgentConfig
    /// {
    ///     ChatClientMiddleware = new()
    ///     {
    ///         (client, services) => new RateLimitingChatClient(client),
    ///         (client, services) => new CostTrackingChatClient(client, services)
    ///     }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore] // Don't serialize middleware delegates
    public List<Func<IChatClient, IServiceProvider?, IChatClient>>? ChatClientMiddleware { get; set; }

    /// <summary>
    /// Builds an Agent from this configuration asynchronously.
    /// 
    /// Convenience method that creates an AgentBuilder from this config and calls Build().
    /// Equivalent to: <c>new AgentBuilder(this).Build()</c>
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the build operation</param>
    /// <returns>A fully constructed Agent ready to use</returns>
    /// <remarks>
    /// This method delegates to AgentBuilder, so all standard build behaviors apply:
    /// - Provider validation (if EnableAsyncValidation is true)
    /// - Tool registration
    /// - Middleware setup
    /// - Event handler registration
    /// </remarks>
    public Task<Agent> BuildAsync(CancellationToken cancellationToken = default)
    {
        var builder = new AgentBuilder(this);
        return builder.BuildAsync(cancellationToken);
    }

    /// <summary>
    /// Loads a JSON configuration file, deserializes it, and immediately builds an Agent asynchronously.
    /// Convenience method combining file loading, deserialization, and agent building in one call.
    /// </summary>
    /// <param name="filePath">Path to the JSON configuration file</param>
    /// <param name="cancellationToken">Cancellation token for file I/O and agent building</param>
    /// <returns>A fully constructed Agent ready to use</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    /// <exception cref="JsonException">Thrown when JSON parsing fails</exception>
    /// <example>
    /// <code>
    /// // Load config.json, deserialize to AgentConfig, and build Agent in one call
    /// var agent = await AgentConfig.BuildFromFileAsync("agent-config.json");
    /// </code>
    /// </example>
    public static async Task<Agent> BuildFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var config = await JsonSerializer.DeserializeAsync<AgentConfig>(stream, HPDJsonContext.Default.AgentConfig, cancellationToken)
            .ConfigureAwait(false);
        
        if (config == null)
        {
            throw new InvalidOperationException($"Failed to deserialize AgentConfig from {filePath}");
        }
        
        return await config.BuildAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a JSON configuration file, deserializes it, and immediately builds an Agent.
    /// Convenience method combining file loading, deserialization, and agent building in one call.
    /// </summary>
    /// <param name="filePath">Path to the JSON configuration file</param>
    /// <returns>A fully constructed Agent ready to use</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    /// <exception cref="JsonException">Thrown when JSON parsing fails</exception>
    /// <remarks>
    /// Note: This is a sync-to-async bridge. For better performance, prefer BuildFromFileAsync() to avoid blocking threads.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Load config.json, deserialize to AgentConfig, and build Agent in one call
    /// var agent = AgentConfig.BuildFromFile("agent-config.json");
    /// </code>
    /// </example>
    public static Agent BuildFromFile(string filePath)
    {
        return BuildFromFileAsync(filePath).ConfigureAwait(false).GetAwaiter().GetResult();
    }
}

#region Supporting Configuration Classes

/// <summary>
/// Configuration for the Model Context Protocol (MCP).
/// </summary>
public class McpConfig
{
    public string ManifestPath { get; set; } = string.Empty;
    /// <summary>
    /// MCP configuration options (stored as object to avoid circular dependency on HPD-Agent.MCP)
    /// </summary>
    public object? Options { get; set; }
}

/// <summary>
/// Configuration for AI provider settings.
/// Based on existing patterns in AgentBuilder.
/// </summary>
public class ProviderConfig
{
    /// <summary>
    /// Provider identifier (lowercase, e.g., "openai", "anthropic", "ollama").
    /// This is the primary key for provider resolution.
    /// </summary>
    public string ProviderKey { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    [JsonIgnore] // ChatOptions contains non-serializable types (tools, response format, etc.)
    public ChatOptions? DefaultChatOptions { get; set; }

    /// <summary>
    /// Custom HTTP headers to include in all requests to this provider.
    /// Used for OAuth flows that require additional headers (e.g., ChatGPT-Account-Id for OpenAI Codex).
    /// </summary>
    public Dictionary<string, string>? CustomHeaders { get; set; }

    /// <summary>
    /// Provider-specific configuration as raw JSON string.
    /// This is the preferred way for FFI/JSON configuration.
    /// The JSON is deserialized using the provider's registered deserializer.
    ///
    /// Example JSON config:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "anthropic",
    ///     "ModelName": "claude-sonnet-4-5-20250929",
    ///     "ApiKey": "sk-ant-...",
    ///     "ProviderOptionsJson": "{\"ThinkingBudgetTokens\":4096,\"EnablePromptCaching\":true}"
    ///   }
    /// }
    /// </code>
    /// </summary>
    public string? ProviderOptionsJson { get; set; }

    /// <summary>
    /// Provider-specific configuration as key-value pairs.
    /// Legacy approach - prefer ProviderOptionsJson for FFI compatibility.
    /// See provider documentation for available options.
    ///
    /// Examples:
    /// - OpenAI: { "Organization": "org-123", "StrictJsonSchema": true }
    /// - Anthropic: { "PromptCachingType": "AutomaticToolsAndSystem" }
    /// - OpenRouter: { "HttpReferer": "https://myapp.com" }
    /// - Ollama: { "NumCtx": 8192, "KeepAlive": "5m" }
    /// </summary>
    public Dictionary<string, object>? AdditionalProperties { get; set; }

    // Cache for deserialized provider config (avoids repeated deserialization)
    [System.Text.Json.Serialization.JsonIgnore]
    private object? _cachedProviderConfig;

    /// <summary>
    /// Gets the provider-specific configuration using the registered deserializer.
    /// Prefers ProviderOptionsJson (FFI-friendly), falls back to AdditionalProperties.
    /// Uses the provider's registered deserializer from ProviderDiscovery for AOT compatibility.
    ///
    /// Usage in providers:
    /// <code>
    /// var myConfig = config.GetTypedProviderConfig&lt;AnthropicProviderConfig&gt;();
    /// </code>
    /// </summary>
    /// <typeparam name="T">The strongly-typed configuration class</typeparam>
    /// <returns>Parsed configuration object, or null if no config is present</returns>
    [RequiresUnreferencedCode("Provider configuration deserialization requires runtime type information. For AOT, use ProviderOptionsJson with registered deserializer.")]
    public T? GetTypedProviderConfig<T>() where T : class
    {
        // Return cached value if available and correct type
        if (_cachedProviderConfig is T cached)
            return cached;

        // Priority 1: Use ProviderOptionsJson with registered deserializer
        if (!string.IsNullOrWhiteSpace(ProviderOptionsJson))
        {
            var registration = Providers.ProviderDiscovery.GetProviderConfigType(ProviderKey);
            if (registration != null && registration.ConfigType == typeof(T))
            {
                var result = registration.Deserialize(ProviderOptionsJson) as T;
                _cachedProviderConfig = result;
                return result;
            }
        }

        // Priority 2: Fall back to AdditionalProperties (legacy)
        var legacyConfig = GetProviderConfig<T>();
        _cachedProviderConfig = legacyConfig;
        return legacyConfig;
    }

    /// <summary>
    /// Sets the provider-specific configuration and updates ProviderOptionsJson.
    /// Uses the provider's registered serializer from ProviderDiscovery for AOT compatibility.
    /// </summary>
    /// <typeparam name="T">The strongly-typed configuration class</typeparam>
    /// <param name="config">The configuration object to set</param>
    public void SetTypedProviderConfig<T>(T config) where T : class
    {
        _cachedProviderConfig = config;

        // Serialize using registered serializer
        var registration = Providers.ProviderDiscovery.GetProviderConfigType(ProviderKey);
        if (registration != null && registration.ConfigType == typeof(T))
        {
            ProviderOptionsJson = registration.Serialize(config);
        }
    }

    /// <summary>
    /// Deserializes AdditionalProperties to a strongly-typed configuration class.
    /// Legacy method - prefer GetTypedProviderConfig for FFI/AOT compatibility.
    ///
    /// Usage in providers:
    /// <code>
    /// var myConfig = config.GetProviderConfig&lt;MyProviderConfig&gt;();
    /// </code>
    /// </summary>
    /// <typeparam name="T">The strongly-typed configuration class</typeparam>
    /// <returns>Parsed configuration object, or null if AdditionalProperties is empty</returns>
    /// <exception cref="InvalidOperationException">Thrown when configuration parsing fails</exception>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Generic deserialization requires runtime type information. Use type-safe provider config methods for AOT.")]
    public T? GetProviderConfig<T>() where T : class
    {
        if (AdditionalProperties == null || AdditionalProperties.Count == 0)
            return null;

        try
        {
            // Convert dictionary to JSON using source-generated context (AOT-safe)
            var json = System.Text.Json.JsonSerializer.Serialize(
                AdditionalProperties, 
                typeof(Dictionary<string, object>),
                HPDJsonContext.Default);
            
            // Deserialize using source-generated context for AOT compatibility
            var result = System.Text.Json.JsonSerializer.Deserialize(
                json,
                typeof(T),
                HPDJsonContext.Default) as T;
            return result;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse provider configuration for {typeof(T).Name}. " +
                $"Please check that your AdditionalProperties match the expected structure. " +
                $"Error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Unexpected error parsing provider configuration for {typeof(T).Name}: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Configuration for provider validation behavior during agent building.
/// </summary>
public class ValidationConfig
{
    /// <summary>
    /// Whether to perform async validation (network calls) during agent building.
    /// 
    /// ⚡ Performance Impact:
    /// - true: Validates API keys and credits via network calls (2-5+ seconds)
    /// - false: Skip network validation for instant builds (recommended for development)
    /// 
    ///  Recommended Usage:
    /// - Development/Testing: false (fast iteration)
    /// - Production/CI: true (catch issues early)
    /// </summary>
    public bool EnableAsyncValidation { get; set; } = false;

    /// <summary>
    /// Timeout for async validation operations in milliseconds.
    /// Only applies when EnableAsyncValidation is true.
    /// </summary>
    public int TimeoutMs { get; set; } = 3000; // 3 seconds

    /// <summary>
    /// Whether to fail agent building if validation fails.
    /// When false, validation failures are logged but don't prevent building.
    /// </summary>
    public bool FailOnValidationError { get; set; } = false;
}

/// <summary>
/// Configuration for error handling behavior.
/// </summary>
public class ErrorHandlingConfig
{
    /// <summary>
    /// Whether to normalize provider-specific errors into standard formats
    /// </summary>
    public bool NormalizeErrors { get; set; } = true;

    /// <summary>
    /// Whether to include provider-specific details in error messages
    /// </summary>
    public bool IncludeProviderDetails { get; set; } = false;

    /// <summary>
    /// Whether to include detailed exception messages in function results sent to the LLM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Security Warning:</b> Setting this to <c>true</c> may expose sensitive information to the LLM and end users:
    /// - Database connection strings
    /// - File system paths
    /// - API keys or tokens
    /// - Internal implementation details
    /// </para>
    /// <para>
    /// When <c>false</c> (default), function errors are reported to the LLM as generic messages like
    /// "Error: Function 'X' failed." The full exception is still available to application code via
    /// <see cref="FunctionResultContent.Exception"/> for logging and debugging.
    /// </para>
    /// <para>
    /// When <c>true</c>, the full exception message is included in the function result, allowing the LLM
    /// to potentially self-correct (e.g., retry with different arguments). Use this only in trusted
    /// environments or with sanitized exceptions.
    /// </para>
    /// <para>
    /// </remarks>
    public bool IncludeDetailedErrorsInChat { get; set; } = true;

    /// <summary>
    /// Maximum number of retries for transient errors
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Timeout for a single function execution (default: 30 seconds)
    /// </summary>
    public TimeSpan? SingleFunctionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Delay before retrying failed function (default: 1 second, exponentially increased per attempt)
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to use provider-specific retry delays from Retry-After headers or error messages.
    /// Default is true (opinionated: respect provider guidance).
    /// </summary>
    public bool UseProviderRetryDelays { get; set; } = true;

    /// <summary>
    /// Whether to automatically attempt token refresh on 401 authentication errors.
    /// Default is true (opinionated: auto-recovery when possible).
    /// </summary>
    public bool AutoRefreshTokensOn401 { get; set; } = true;

    /// <summary>
    /// Maximum retry delay cap to prevent excessive waiting.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Exponential backoff multiplier for retry delays.
    /// Default is 2.0 (doubles the delay each attempt).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Optional per-category retry limits. If null, uses MaxRetries for all categories.
    /// Example: { ErrorCategory.RateLimitRetryable: 5, ErrorCategory.ServerError: 3 }
    /// </summary>
    public Dictionary<HPD.Agent.ErrorHandling.ErrorCategory, int>? MaxRetriesByCategory { get; set; }
    /// <summary>
    /// Custom retry strategy that overrides default behavior.
    /// Parameters: (exception, attemptNumber, cancellationToken)
    /// Returns: TimeSpan for retry delay, or null to stop retrying.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<Exception, int, CancellationToken, Task<TimeSpan?>>? CustomRetryStrategy { get; set; }
}

/// <summary>
/// Configuration for document handling behavior.
/// NOTE: This config is legacy and will be deprecated in favor of DocumentHandlingOptions.
/// Use WithDocumentHandling() middleware extension instead.
/// </summary>
public class DocumentHandlingConfig
{
    /// <summary>
    /// Custom document tag format for message injection.
    /// Uses string.Format with {0} = filename, {1} = extracted text.
    /// If null, uses default format: "\n\n[ATTACHED_DOCUMENT[{0}]]\n{1}\n[/ATTACHED_DOCUMENT]\n\n"
    /// </summary>
    public string? DocumentTagFormat { get; set; }

    /// <summary>
    /// Maximum file size in bytes to process (default: 10MB).
    /// Files larger than this will be rejected.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Configuration for conversation history reduction using Microsoft.Extensions.AI IChatReducer.
/// </summary>
public class HistoryReductionConfig
{
    /// <summary>
    /// Whether history reduction is enabled.
    /// Default is false to maintain backward compatibility.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Strategy for reducing conversation history.
    /// Default is MessageCounting (keeps last N messages).
    /// </summary>
    public HistoryReductionStrategy Strategy { get; set; } = HistoryReductionStrategy.MessageCounting;

    /// <summary>
    /// Behavior when history reduction is triggered.
    /// - Continue (default): Reduction happens transparently, agent continues immediately
    /// - CircuitBreaker: Reduction terminates the turn, user must explicitly continue
    /// Can be overridden per-turn via AgentRunConfig.HistoryReductionBehaviorOverride.
    /// </summary>
    public HistoryReductionBehavior Behavior { get; set; } = HistoryReductionBehavior.Continue;

    /// <summary>
    /// Unit used to measure history depth for threshold comparisons.
    /// Default: Exchanges (one RunAsync call = one user↔agent exchange).
    /// </summary>
    public HistoryCountingUnit CountingUnit { get; set; } = HistoryCountingUnit.Exchanges;

    /// <summary>
    /// Target count to retain after reduction. Unit determined by <see cref="CountingUnit"/>.
    /// With CountingUnit.Exchanges (default): number of user↔agent exchanges to keep.
    /// With CountingUnit.Messages: number of raw ChatMessage objects to keep.
    /// Default is 20.
    /// </summary>
    public int TargetCount { get; set; } = 20;

    /// <summary>
    /// Threshold count for SummarizingChatReducer.
    /// Number of units (exchanges or messages, per CountingUnit) allowed beyond TargetCount before summarization is triggered.
    /// Only used when Strategy is Summarizing. Default is 5.
    /// </summary>
    public int? SummarizationThreshold { get; set; } = 5;

    /// <summary>
    /// Target token count after reduction (default: 4000).
    ///
    ///   CURRENTLY IGNORED - Token tracking is not implemented.
    /// </summary>
    public int TargetTokenBudget { get; set; } = 4000;

    /// <summary>
    /// Token threshold for triggering reduction when using token budgets.
    /// Number of tokens allowed beyond TargetTokenBudget before reduction is triggered.
    ///
    ///   CURRENTLY IGNORED - Token tracking is not implemented.
    /// </summary>
    public int TokenBudgetThreshold { get; set; } = 1000;

    /// <summary>
    /// When set, uses percentage-based triggers instead of absolute token counts.
    /// Requires ContextWindowSize to be configured.
    /// Example: 0.7 = trigger reduction at 70% of context window.
    ///
    ///   CURRENTLY IGNORED - Token tracking is not implemented.
    /// Use TargetCount instead for reliable history reduction.
    /// </summary>
    public double? TokenBudgetTriggerPercentage { get; set; } = null;

    /// <summary>
    /// Percentage of context window to preserve after reduction.
    /// Only used when TokenBudgetTriggerPercentage is set.
    /// Example: 0.3 = keep 30% of context window after compression.
    ///
    ///   CURRENTLY IGNORED - Token tracking is not implemented.
    /// </summary>
    public double TokenBudgetPreservePercentage { get; set; } = 0.3;

    /// <summary>
    /// Context window size for percentage calculations.
    /// Required when using TokenBudgetTriggerPercentage.
    ///
    ///   CURRENTLY IGNORED - Token tracking is not implemented.
    /// </summary>
    public int? ContextWindowSize { get; set; } = null;

    /// <summary>
    /// Custom summarization prompt for SummarizingChatReducer.
    /// If null, uses the default prompt from Microsoft.Extensions.AI.
    /// Only used when Strategy is Summarizing.
    /// </summary>
    public string? CustomSummarizationPrompt { get; set; }

    /// <summary>
    /// Optional separate provider configuration for the summarization LLM.
    /// If null, uses the agent's main provider (baseClient).
    /// Useful for cost optimization - e.g., use GPT-4o-mini for summaries while main agent uses GPT-4.
    /// Only used when Strategy is Summarizing.
    /// </summary>
    public ProviderConfig? SummarizerProvider { get; set; }

    /// <summary>
    /// Whether to use a single comprehensive summary (re-summarize everything including old summary)
    /// or maintain layered summaries (incremental summarization).
    /// Default is true (single summary for better quality, following Semantic Kernel pattern).
    /// </summary>
    public bool UseSingleSummary { get; set; } = true;
}

/// <summary>
/// Strategy for reducing conversation history size.
/// </summary>
public enum HistoryReductionStrategy
{
    /// <summary>
    /// Keep only the N most recent messages (plus first system message).
    /// Fast and simple, but loses older context completely.
    /// </summary>
    MessageCounting,

    /// <summary>
    /// Use LLM to summarize older messages when history exceeds threshold.
    /// Preserves context through summarization, but requires additional LLM calls.
    /// </summary>
    Summarizing
}

/// <summary>
/// Behavior when history reduction is triggered.
/// Controls whether the agent continues immediately or stops for user confirmation.
/// </summary>
public enum HistoryReductionBehavior
{
    /// <summary>
    /// Continue immediately after reduction (default).
    /// Reduction happens transparently without interrupting the agent flow.
    /// Use when: Reduction is an implementation detail, users don't need to know.
    /// </summary>
    Continue,

    /// <summary>
    /// Stop execution after reduction and require user confirmation to continue.
    /// Acts as a circuit breaker - reduction terminates the current turn.
    /// Use when: Users need to be aware of context loss, review summary, or save important info.
    /// </summary>
    CircuitBreaker
}

/// <summary>
/// Unit used to measure conversation history depth for history reduction thresholds.
/// </summary>
public enum HistoryCountingUnit
{
    /// <summary>
    /// Count RunAsync calls (one per user↔agent exchange, regardless of internal tool call depth).
    /// Default. TargetCount=20 means "keep 20 conversation exchanges."
    /// </summary>
    Exchanges,

    /// <summary>
    /// Count raw ChatMessage protocol objects.
    /// Use if you need fine-grained control and understand the LLM message protocol.
    /// Equivalent to the behaviour before this feature was introduced.
    /// </summary>
    Messages
}



/// <summary>
/// Configuration for agentic loop safety controls to prevent runaway execution.
/// </summary>
public class AgenticLoopConfig
{
    /// <summary>
    /// Maximum duration for a single turn before timeout (default: 5 minutes)
    /// </summary>
    public TimeSpan? MaxTurnDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of functions to execute in parallel (default: null = unlimited).
    /// Useful for limiting resource consumption when functions are CPU-intensive,
    /// respecting external API rate limits, or matching database connection pool sizes.
    /// </summary>
    public int? MaxParallelFunctions { get; set; } = null;

    /// <summary>
    /// Controls behavior when the LLM requests a function that isn't available.
    /// When false (default): Creates a "function not found" error message and continues the agentic loop.
    /// When true: Terminates the agentic loop immediately and returns control to the caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setting this to false (default) allows the LLM to recover from hallucinated functions by seeing
    /// the error and trying different approaches. This is useful for normal single-agent scenarios.
    /// </para>
    /// <para>
    /// Setting this to true is useful for multi-agent handoff scenarios where an unknown function request
    /// might indicate that the current agent should transfer control to another agent that has that function.
    /// When the loop terminates, the caller receives the function call request and can route it appropriately.
    /// </para>
    /// <para>
    /// Note: Functions that are known (via ChatOptions.Tools or AgentConfig.ServerConfiguredTools) but aren't
    /// AIFunction instances (e.g., AIFunctionDeclaration only) will also cause termination regardless of
    /// this setting, as they cannot be invoked by the agent.
    /// </para>
    /// </remarks>
    public bool TerminateOnUnknownCalls { get; set; } = false;
}

/// <summary>
/// Configuration for tool selection behavior.
/// FFI-friendly: Uses primitives (strings) instead of complex types for cross-language compatibility.
/// </summary>
public class ToolSelectionConfig
{
    /// <summary>
    /// Tool selection mode: "Auto" (LLM decides), "None" (no tools), "RequireAny" (must call at least one), or "RequireSpecific" (must call the named function).
    /// Default is "Auto".
    /// </summary>
    public string ToolMode { get; set; } = "Auto";

    /// <summary>
    /// Required function name when ToolMode = "RequireSpecific".
    /// Ignored for other modes.
    /// </summary>
    public string? RequiredFunctionName { get; set; }
}

/// <summary>
/// Mistral AI-specific settings
/// </summary>
public class MistralSettings
{
    /// <summary>
    /// API key for the Mistral AI platform.
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Controls where skill instructions are injected during skill execution.
/// Iteration filter ALWAYS injects to system prompt - this controls whether to ALSO include in function result.
/// </summary>
public enum SkillInstructionMode
{
    /// <summary>
    /// Instructions only in system prompt via iteration filter (function result has activation message only).
    /// Most token efficient - instructions appear once in system prompt.
    /// Recommended: Use this mode to avoid redundant instructions in conversation history.
    /// </summary>
    PromptMiddlewareOnly,

    /// <summary>
    /// Instructions in BOTH system prompt (via iteration filter) AND function result (redundant double emphasis).
    /// Uses more tokens but may improve LLM compliance for complex skills.
    /// Default for backward compatibility.
    /// </summary>
    Both
}

/// <summary>
/// Configuration for Collapsing feature.
/// Controls hierarchical organization of functions to reduce token usage.
/// </summary>
public class CollapsingConfig
{
    /// <summary>
    /// Enable Collapsing for C# Toolkits. When true, Toolkit functions are hidden behind container functions.
    /// Default: true (enabled - Toolkits with [Collapse] attribute are collapsed).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Enable Collapsing for Client (AGUI) tools. When true, all Client tools are grouped in a ClientTools container.
    /// Client tools are human-in-the-loop tools executed by the UI.
    /// Default: false (Client tools always visible).
    /// </summary>
    public bool CollapseClientTools { get; set; } = false;

    /// <summary>
    /// Maximum number of function names to include in auto-generated container descriptions.
    /// For template descriptions like "MCP Server 'filesystem'. Contains 15 functions: ReadFile, WriteFile, ..."
    /// Default: 10.
    /// </summary>
    public int MaxFunctionNamesInDescription { get; set; } = 10;

    /// <summary>
    /// Controls whether skill instructions appear in function result (in addition to system prompt).
    /// Iteration filter ALWAYS injects to system prompt - this controls redundancy in function result.
    /// - PromptMiddlewareOnly: Instructions only in system prompt (most token efficient, recommended)
    /// - Both: Instructions in both system prompt AND function result (backward compatibility)
    /// Default: Both (for backward compatibility, but PromptMiddlewareOnly is recommended).
    /// </summary>
    public SkillInstructionMode SkillInstructionMode { get; set; } = SkillInstructionMode.Both;

    /// <summary>
    /// WhetherSystemPrompt injections persist across message turns.
    /// Default: false (instructions cleared at end of each message turn for clean prompts).
    ///
    /// Set to true if you need container instructions to remain in system prompt even after
    /// the message turn ends. WARNING: Can cause prompt bloat in long conversations.
    /// </summary>
    public bool PersistSystemPromptInjections { get; set; } = false;

    /// <summary>
    /// Enable automatic error recovery for [Collapse] containers (Container Transparency V2).
    /// When true, calling a hidden function automatically expands its parent container silently.
    /// This allows smaller models to work seamlessly without understanding container mechanics.
    /// Default: true (enabled).
    /// </summary>
    public bool EnableErrorRecovery { get; set; } = true;

    /// <summary>
    /// Optional post-expansion instructions for specific MCP servers.
    /// Key = MCP server name (e.g., "filesystem", "github")
    /// Value = Instructions shown to the agent after that server's container is expanded.
    /// Example: { "filesystem", "IMPORTANT: Always use absolute paths. Check FileExists before operations." }
    /// </summary>
    public Dictionary<string, string>? MCPServerInstructions { get; set; }

    /// <summary>
    /// Optional post-expansion instructions for Client tools container.
    /// Shown to the agent after expanding the ClientTools container.
    /// Example: "These tools interact with the user. Use ConfirmAction for destructive operations."
    /// </summary>
    public string? ClientToolsInstructions { get; set; }

    /// <summary>
    /// List of toolkit names that should never be collapsed, even if they have containers.
    /// This is a runtime override that works even if the toolkit was compiled with collapse support.
    /// Use this to force specific toolkits to always show their functions directly.
    /// Example: new HashSet&lt;string&gt; { "MathToolkit", "CoreTools" }
    /// </summary>
    public HashSet<string>? NeverCollapse { get; set; }

}
/// <summary>
/// Configuration for agent system messages.
/// Allows customization of messages for internationalization, branding, or context-specific needs.
/// </summary>
public class AgentMessagesConfig
{
    /// <summary>
    /// Message shown when the maximum iteration limit is reached.
    /// Placeholders: {maxIterations}
    /// Default: "Maximum iteration limit reached ({maxIterations} iterations). The agent was unable to complete the task within the allowed number of turns."
    /// </summary>
    public string MaxIterationsReached { get; set; } =
        "Maximum iteration limit reached ({maxIterations} iterations). The agent was unable to complete the task within the allowed number of turns.";

    /// <summary>
    /// Message shown when circuit breaker triggers due to repeated identical tool calls.
    /// Placeholders: {toolName}, {count}
    /// Default: "Circuit breaker triggered: '{toolName}' called {count} times with the same arguments. This may indicate the agent is stuck in a loop."
    /// </summary>
    public string CircuitBreakerTriggered { get; set; } =
        "Circuit breaker triggered: '{toolName}' called {count} times with the same arguments. This may indicate the agent is stuck in a loop.";

    /// <summary>
    /// Message shown when maximum consecutive errors is exceeded.
    /// Placeholders: {maxErrors}
    /// Default: "Exceeded maximum consecutive errors ({maxErrors}). The agent is unable to proceed due to repeated failures."
    /// </summary>
    public string MaxConsecutiveErrors { get; set; } =
        "Exceeded maximum consecutive errors ({maxErrors}). The agent is unable to proceed due to repeated failures.";

    /// <summary>
    /// Default message sent to LLM when a tool execution is denied by permission filter without a custom reason.
    /// This is used when user denies permission but doesn't provide a specific denial reason.
    /// Set to empty string if you want no message sent to LLM.
    /// Default: "Permission denied by user."
    /// </summary>
    public string PermissionDeniedDefault { get; set; } =
        "Permission denied by user.";

    /// <summary>
    /// Formats the max iterations message with the actual value.
    /// </summary>
    public string FormatMaxIterationsReached(int maxIterations)
    {
        return MaxIterationsReached.Replace("{maxIterations}", maxIterations.ToString());
    }

    /// <summary>
    /// Formats the circuit breaker message with tool name and count.
    /// </summary>
    public string FormatCircuitBreakerTriggered(string toolName, int count)
    {
        return CircuitBreakerTriggered
            .Replace("{toolName}", toolName)
            .Replace("{count}", count.ToString());
    }

    /// <summary>
    /// Formats the max consecutive errors message with the actual value.
    /// </summary>
    public string FormatMaxConsecutiveErrors(int maxErrors)
    {
        return MaxConsecutiveErrors.Replace("{maxErrors}", maxErrors.ToString());
    }
}

/// <summary>
/// Distributed caching configuration for LLM response caching.
/// </summary>
public class CachingConfig
{
    /// <summary>
    /// Enable distributed caching.
    /// Default: false (opt-in)
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why opt-in?</b>
    /// - Requires external IDistributedCache implementation (Redis, Memory, etc.)
    /// - Changes runtime behavior (cache hits bypass LLM calls)
    /// - Needs proper cache invalidation strategy
    /// </para>
    /// <para>
    /// When enabled, identical requests will return cached responses,
    /// dramatically reducing latency and cost for repeated queries.
    /// </para>
    /// </remarks>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Coalesce streaming responses for storage efficiency.
    /// When true, stores final response (space-efficient).
    /// When false, stores full streaming updates (high-fidelity replay).
    /// Default: true
    /// </summary>
    public bool CoalesceStreamingUpdates { get; set; } = true;

    /// <summary>
    /// Allow caching when ConversationId is set (stateful conversations).
    /// Default: false (prevents stale data in multi-turn conversations)
    /// </summary>
    /// <remarks>
    /// Setting this to true can cause issues:
    /// - Cached responses may not reflect conversation state changes
    /// - Updates to conversation history won't invalidate cache
    /// Only enable if you understand the implications.
    /// </remarks>
    public bool CacheStatefulConversations { get; set; } = false;

    /// <summary>
    /// Cache entry TTL (time-to-live).
    /// Default: 30 minutes
    /// </summary>
    public TimeSpan? CacheExpiration { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Configuration for background responses behavior.
/// Enables long-running LLM operations to avoid HTTP gateway timeouts by returning
/// immediately with a continuation token that can be polled for completion.
/// </summary>
/// <remarks>
/// <para>
/// <b>When to use:</b>
/// - Behind API gateways with timeout limits (AWS API Gateway: 30s, ALB: 60s)
/// - In serverless functions with execution time limits
/// - For mobile/unreliable connections that may drop during long operations
/// - For long-running generations (essays, comprehensive analysis, etc.)
/// </para>
/// <para>
/// <b>How it works:</b>
/// When AllowBackgroundResponses is true and the provider supports it:
/// 1. Provider starts operation and returns immediately with a token
/// 2. Operation continues on provider's infrastructure
/// 3. Client polls with the token to check status/get result
/// </para>
/// </remarks>
public class BackgroundResponsesConfig
{
    /// <summary>
    /// Default value for AllowBackgroundResponses when not specified per-invocation.
    /// Default: false (traditional blocking behavior)
    /// </summary>
    public bool DefaultAllow { get; set; } = false;

    /// <summary>
    /// Default polling interval when client needs to poll for results.
    /// Providers may have minimum intervals; this is a hint.
    /// Default: 2 seconds
    /// </summary>
    public TimeSpan DefaultPollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum time to wait for a background operation to complete.
    /// Null = no timeout (wait indefinitely).
    /// Default: null
    /// </summary>
    public TimeSpan? DefaultTimeout { get; set; } = null;

    /// <summary>
    /// Whether to automatically poll until completion (convenience mode).
    /// When true, RunAsync blocks but internally uses background + polling.
    /// Provides timeout resilience without changing caller code.
    /// Default: false
    /// </summary>
    public bool AutoPollToCompletion { get; set; } = false;

    /// <summary>
    /// Maximum number of poll attempts before giving up.
    /// Only applies when AutoPollToCompletion is true.
    /// Default: 1000 (with 2s interval = ~33 minutes)
    /// </summary>
    public int MaxPollAttempts { get; set; } = 1000;
}

/// <summary>
/// Lightweight proxy for HPD.Agent.Audio.AudioConfig to avoid circular dependency.
/// This class lives in the core HPD-Agent package and is used by AgentConfig.
/// The actual AudioConfig with full V3 role-based architecture (TtsConfig, SttConfig, VadConfig)
/// lives in HPD-Agent.Audio package and is referenced by the audio middleware.
///
/// Module initializers in audio provider packages (HPD-Agent.AudioProviders.*)
/// register their factories with TtsProviderDiscovery, SttProviderDiscovery, VadProviderDiscovery.
/// </summary>
public class AudioConfig
{
    // This is intentionally minimal - just enough for serialization/FFI
    // The HPD.Agent.Audio.AudioConfig class contains the full V3 implementation
}

#endregion

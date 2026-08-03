using System;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Serialization;

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

    /// <summary>Maximum nested subagent depth. Root agents are depth zero.</summary>
    public int MaxSubAgentDepth { get; set; } = 4;

    /// <summary>
    /// How many additional turns to allow when user chooses to continue beyond the limit.
    /// This includes extra iterations for the LLM to complete its task and generate a final response.
    /// </summary>
    public int ContinuationExtensionAmount { get; set; } = 3;

    /// <summary>
    /// Configuration for provider-created client families.
    /// </summary>
    public AgentClientsConfig Clients { get; set; } = new();

    /// <summary>Gets or sets provider-indexed family defaults used when a run switches providers.</summary>
    public IDictionary<string, AgentProviderProfile> ProviderProfiles { get; set; }
        = new Dictionary<string, AgentProviderProfile>(StringComparer.OrdinalIgnoreCase);

    public void SetClientConfig(HPD.Agent.Providers.ProviderClientFamily family, ProviderClientConfig? config)
    {
        Clients ??= new AgentClientsConfig();
        Clients.SetFamilyConfig(family, config);
    }

    public void SetChatClientConfig(ChatClientConfig? config) =>
        SetClientConfig(HPD.Agent.Providers.ProviderClientFamily.Chat, config);

    public ProviderClientConfig EnsureClientConfig(HPD.Agent.Providers.ProviderClientFamily family)
    {
        Clients ??= new AgentClientsConfig();

        var config = Clients.GetFamilyConfig(family);
        if (config is not null)
            return config;

        config = family switch
        {
            HPD.Agent.Providers.ProviderClientFamily.Chat => new ChatClientConfig(),
            HPD.Agent.Providers.ProviderClientFamily.Realtime => new RealtimeClientConfig(),
            HPD.Agent.Providers.ProviderClientFamily.ImageGeneration => new ImageGenerationClientConfig(),
            HPD.Agent.Providers.ProviderClientFamily.Embeddings => new EmbeddingsClientConfig(),
            HPD.Agent.Providers.ProviderClientFamily.TextToSpeech => new TextToSpeechClientConfig(),
            HPD.Agent.Providers.ProviderClientFamily.SpeechToText => new SpeechToTextClientConfig(),
            HPD.Agent.Providers.ProviderClientFamily.HostedFiles => new HostedFilesClientConfig(),
            HPD.Agent.Providers.ProviderClientFamily.VoiceActivityDetection => new VoiceActivityDetectionClientConfig(),
            HPD.Agent.Providers.ProviderClientFamily.EndOfTurnDetection => new EndOfTurnDetectionClientConfig(),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown provider client family.")
        };
        Clients.SetFamilyConfig(family, config);
        return config;
    }

    public ChatClientConfig EnsureChatClientConfig() =>
        (ChatClientConfig)EnsureClientConfig(HPD.Agent.Providers.ProviderClientFamily.Chat);

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
    /// Configuration for conversation compaction to manage context window size.
    /// </summary>
    public CompactionConfig? Compaction { get; set; }

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
    /// Configuration for harness collapsing, which organizes functions hierarchically to reduce token usage.
    /// Collapsing is enabled by default when this property is omitted from serialized configuration.
    /// Set <see cref="CollapsingConfig.Enabled"/> to <see langword="false"/> to expose functions directly.
    /// This property cannot be <see langword="null"/>.
    /// </summary>
    public CollapsingConfig Collapsing
    {
        get => _collapsing;
        set => _collapsing = value ?? throw new ArgumentNullException(nameof(value),
            "Agent collapsing configuration cannot be null. Omit the property to use the enabled default, or set enabled to false to opt out.");
    }

    private CollapsingConfig _collapsing = new();

    /// <summary>Gets or sets progressive skill runtime behavior.</summary>
    public SkillRuntimeOptions Skills { get; set; } = new();

    /// <summary>
    /// ToolHarnesses to include. Supports both simple string names and rich references.
    /// Resolved via source-generated registry at Build() time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Simple syntax:</b>
    /// <code>
    /// { "toolharnesses": ["MathToolHarness", "SearchToolHarness"] }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Rich syntax:</b>
    /// <code>
    /// {
    ///   "toolharnesses": [
    ///     "MathToolHarness",
    ///     { "name": "FileToolHarness", "functions": ["ReadFile", "WriteFile"] },
    ///     { "name": "ApiToolHarness", "config": { "apiKey": "${API_KEY}" } }
    ///   ]
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    [JsonPropertyName("toolharnesses")]
    public List<ToolHarnessReference> ToolHarnesses { get; set; } = new();

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
    /// Internal: Set of explicitly registered ToolHarness names (for Collapsing manager).
    /// This is set by the builder and used to distinguish explicit vs implicit ToolHarness registration.
    /// </summary>
    [JsonIgnore]
    public ImmutableHashSet<string> explicitlyRegisteredToolHarnesses { get; set; } = ImmutableHashSet<string>.Empty;

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
    /// Whether reasoning content should be included when projecting thread history back into model input.
    /// Default: false (reasoning is recorded in thread events and shown during streaming, but excluded
    /// from model history to save tokens).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Trade-offs:</b>
    /// - false (default): Lower cost, smaller context - reasoning remains in thread events but not future prompts
    /// - true: Higher cost, larger context - reasoning is sent back to the model in future prompts
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
    public bool IncludeReasoningInModelHistory { get; set; } = false;

    public ProviderClientConfig? ResolveClientConfig(
        HPD.Agent.Providers.ProviderClientFamily family,
        AgentClientsConfig? runClients = null)
    {
        return ProviderClientConfigResolver.Resolve(
            Clients,
            family,
            runClients,
            ProviderProfiles);
    }

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
    /// Optional stable identity used by <see cref="AgentBuilder"/> to load and
    /// persist a <see cref="StoredAgent"/> definition through <see cref="AgentStore"/>.
    /// Ignored during JSON serialization because persisted identity lives on
    /// <see cref="StoredAgent.Id"/>.
    /// </summary>
    [JsonIgnore]
    public string? AgentId { get; set; }

    /// <summary>
    /// Optional session store for durable execution and crash recovery.
    /// Use InMemorySessionStore for development/testing or FileSessionStore for production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Example - Auto-save mode:</b>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithSessionStore(new FileSessionStore("./sessions"), autoSave: true)
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
    /// Options for agent definition persistence behavior.
    /// Controls whether <see cref="AgentBuilder.BuildAsync"/> saves the current
    /// definition back to <see cref="AgentStore"/>.
    /// </summary>
    [JsonIgnore]
    public AgentStoreOptions? AgentStoreOptions { get; set; }

    /// <summary>
    /// Options for session persistence behavior.
    /// Controls auto-save, checkpoint frequency, and retention policy.
    /// </summary>
    [JsonIgnore]
    public SessionStoreOptions? SessionStoreOptions { get; set; }

    // Threading config removed - threading is now an application-level concern

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
    public IList<AITool>? ServerConfiguredTools { get; set; } = new List<AITool>();

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
    ///     ClientMiddleware = new()
    ///     {
    ///         Chat = new()
    ///         {
    ///             (client, services) => new RateLimitingChatClient(client),
    ///             (client, services) => new CostTrackingChatClient(client, services)
    ///         }
    ///     }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore] // Don't serialize middleware delegates
    public AgentClientMiddlewareConfig? ClientMiddleware { get; set; }

    /// <summary>
    /// Loads a JSON or YAML configuration file and builds an agent.
    /// </summary>
    public static async Task<Agent> BuildFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var config = await HpdAgentConfigSerializer.ReadFileAsync(filePath, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Failed to deserialize AgentConfig from {filePath}");

        return await new AgentBuilder(config).BuildAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a JSON or YAML configuration file and builds an agent.
    /// </summary>
    public static Agent BuildFromFile(string filePath)
        => BuildFromFileAsync(filePath).ConfigureAwait(false).GetAwaiter().GetResult();

}

#region Supporting Configuration Classes

/// <summary>
/// Configuration for the Model Context Protocol (MCP).
/// </summary>
public class McpConfig
{
    public string ManifestPath { get; set; } = string.Empty;
    public string ManifestContent { get; set; } = string.Empty;
    /// <summary>
    /// Runtime-only MCP configuration options.
    /// Stored as object to avoid a compile-time dependency on HPD-Agent.MCP.
    /// </summary>
    [JsonIgnore]
    public object? Options { get; set; }
}

/// <summary>
/// Configuration for AI provider settings.
/// Based on existing patterns in AgentBuilder.
/// </summary>
public class ProviderClientConfig
{
    /// <summary>
    /// Provider identifier (lowercase, e.g., "openai", "anthropic", "ollama").
    /// This is the primary key for provider resolution.
    /// </summary>
    public string? ProviderKey { get; set; }

    public string? ModelName { get; set; }
    /// <summary>
    /// Gets or sets a runtime-only API-key override.
    /// Secret values are deliberately excluded from serialized provider configuration.
    /// </summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the opaque name of a host-registered static credential.
    /// </summary>
    public string? AuthenticationKey { get; set; }
    public string? Endpoint { get; set; }

    /// <summary>
    /// Custom HTTP headers to include in all requests to this provider.
    /// Authentication headers are rejected; use static authentication registration instead.
    /// </summary>
    public Dictionary<string, string>? CustomHeaders { get; set; }

    /// <summary>
    /// Provider-specific client-construction configuration as a JSON/YAML object.
    /// The value is deserialized using the provider's registered source-generated deserializer.
    /// </summary>
    public JsonElement? ConstructionOptions { get; set; }

    /// <summary>Gets or sets the strongly typed provider-specific client configuration.</summary>
    public IProviderConfig? ProviderConfig { get; set; }

    /// <summary>
    /// Optional OpenRouter HTTP-Referer attribution header.
    /// </summary>
    public string? HttpReferer { get; set; }

    /// <summary>
    /// Optional OpenRouter X-Title attribution header.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// Optional runtime-only prompt formatter for local providers that expose formatter hooks.
    /// </summary>
    [JsonIgnore]
    public Func<IEnumerable<ChatMessage>, ChatOptions?, string>? PromptFormatter { get; set; }

    public string? GetConstructionOptionsRawJson()
        => ConstructionOptions?.GetRawText();

    public void SetConstructionOptionsRawJson(string? json)
    {
        ConstructionOptions = string.IsNullOrWhiteSpace(json)
            ? null
            : JsonDocument.Parse(json).RootElement.Clone();
    }

}

/// <summary>
/// Provider-created client-family configuration for an agent or a single run.
/// Shared provider defaults live in <see cref="Providers"/> and are merged with
/// family-specific settings when a client family is resolved.
/// </summary>
public class AgentClientsConfig
{
    /// <summary>Gets or sets the model transport that drives the agent turn.</summary>
    public AgentModelTransportMode Transport { get; set; } = AgentModelTransportMode.Auto;

    public ChatClientConfig? Chat { get; set; }
    public TextToSpeechClientConfig? TextToSpeech { get; set; }
    public SpeechToTextClientConfig? SpeechToText { get; set; }
    public RealtimeClientConfig? Realtime { get; set; }
    public ImageGenerationClientConfig? ImageGeneration { get; set; }
    public EmbeddingsClientConfig? Embeddings { get; set; }
    public HostedFilesClientConfig? HostedFiles { get; set; }
    public VoiceActivityDetectionClientConfig? VoiceActivityDetection { get; set; }
    public EndOfTurnDetectionClientConfig? EndOfTurnDetection { get; set; }

    public ProviderClientConfig? GetFamilyConfig(HPD.Agent.Providers.ProviderClientFamily family) =>
        family switch
        {
            HPD.Agent.Providers.ProviderClientFamily.Chat => Chat,
            HPD.Agent.Providers.ProviderClientFamily.TextToSpeech => TextToSpeech,
            HPD.Agent.Providers.ProviderClientFamily.SpeechToText => SpeechToText,
            HPD.Agent.Providers.ProviderClientFamily.Realtime => Realtime,
            HPD.Agent.Providers.ProviderClientFamily.ImageGeneration => ImageGeneration,
            HPD.Agent.Providers.ProviderClientFamily.Embeddings => Embeddings,
            HPD.Agent.Providers.ProviderClientFamily.HostedFiles => HostedFiles,
            HPD.Agent.Providers.ProviderClientFamily.VoiceActivityDetection => VoiceActivityDetection,
            HPD.Agent.Providers.ProviderClientFamily.EndOfTurnDetection => EndOfTurnDetection,
            _ => null
        };

    public void SetFamilyConfig(HPD.Agent.Providers.ProviderClientFamily family, ProviderClientConfig? config)
    {
        switch (family)
        {
            case HPD.Agent.Providers.ProviderClientFamily.Chat:
                Chat = config is null
                    ? null
                    : config as ChatClientConfig ?? throw new ArgumentException(
                        $"Chat configuration must be {nameof(ChatClientConfig)}.", nameof(config));
                break;
            case HPD.Agent.Providers.ProviderClientFamily.TextToSpeech:
                TextToSpeech = RequireFamilyType<TextToSpeechClientConfig>(config, family);
                break;
            case HPD.Agent.Providers.ProviderClientFamily.SpeechToText:
                SpeechToText = RequireFamilyType<SpeechToTextClientConfig>(config, family);
                break;
            case HPD.Agent.Providers.ProviderClientFamily.Realtime:
                Realtime = RequireFamilyType<RealtimeClientConfig>(config, family);
                break;
            case HPD.Agent.Providers.ProviderClientFamily.ImageGeneration:
                ImageGeneration = RequireFamilyType<ImageGenerationClientConfig>(config, family);
                break;
            case HPD.Agent.Providers.ProviderClientFamily.Embeddings:
                Embeddings = RequireFamilyType<EmbeddingsClientConfig>(config, family);
                break;
            case HPD.Agent.Providers.ProviderClientFamily.HostedFiles:
                HostedFiles = RequireFamilyType<HostedFilesClientConfig>(config, family);
                break;
            case HPD.Agent.Providers.ProviderClientFamily.VoiceActivityDetection:
                VoiceActivityDetection = RequireFamilyType<VoiceActivityDetectionClientConfig>(config, family);
                break;
            case HPD.Agent.Providers.ProviderClientFamily.EndOfTurnDetection:
                EndOfTurnDetection = RequireFamilyType<EndOfTurnDetectionClientConfig>(config, family);
                break;
        }
    }

    private static TConfig? RequireFamilyType<TConfig>(
        ProviderClientConfig? config,
        HPD.Agent.Providers.ProviderClientFamily family)
        where TConfig : ProviderClientConfig
        => config is null
            ? null
            : config as TConfig ?? throw new ArgumentException(
                $"{family} configuration must be {typeof(TConfig).Name}.", nameof(config));
}

/// <summary>Family defaults associated with one provider identity.</summary>
public sealed class AgentProviderProfile
{
    /// <summary>Gets or sets Chat defaults.</summary>
    public ChatClientConfig? Chat { get; set; }
    /// <summary>Gets or sets Realtime defaults.</summary>
    public RealtimeClientConfig? Realtime { get; set; }
    /// <summary>Gets or sets image-generation defaults.</summary>
    public ImageGenerationClientConfig? ImageGeneration { get; set; }
    /// <summary>Gets or sets embedding defaults.</summary>
    public EmbeddingsClientConfig? Embeddings { get; set; }
    /// <summary>Gets or sets text-to-speech defaults.</summary>
    public TextToSpeechClientConfig? TextToSpeech { get; set; }
    /// <summary>Gets or sets speech-to-text defaults.</summary>
    public SpeechToTextClientConfig? SpeechToText { get; set; }
    /// <summary>Gets or sets hosted-file defaults.</summary>
    public HostedFilesClientConfig? HostedFiles { get; set; }

    internal ProviderClientConfig? GetFamilyConfig(HPD.Agent.Providers.ProviderClientFamily family) => family switch
    {
        HPD.Agent.Providers.ProviderClientFamily.Chat => Chat,
        HPD.Agent.Providers.ProviderClientFamily.Realtime => Realtime,
        HPD.Agent.Providers.ProviderClientFamily.ImageGeneration => ImageGeneration,
        HPD.Agent.Providers.ProviderClientFamily.Embeddings => Embeddings,
        HPD.Agent.Providers.ProviderClientFamily.TextToSpeech => TextToSpeech,
        HPD.Agent.Providers.ProviderClientFamily.SpeechToText => SpeechToText,
        HPD.Agent.Providers.ProviderClientFamily.HostedFiles => HostedFiles,
        _ => null
    };
}

/// <summary>
/// Runtime-only wrappers for provider-created client families.
/// </summary>
public class AgentClientMiddlewareConfig
{
    public List<Func<IChatClient, IServiceProvider?, IChatClient>>? Chat { get; set; }
    public List<Func<ITextToSpeechClient, IServiceProvider?, ITextToSpeechClient>>? TextToSpeech { get; set; }
    public List<Func<ISpeechToTextClient, IServiceProvider?, ISpeechToTextClient>>? SpeechToText { get; set; }
    public List<Func<IRealtimeClient, IServiceProvider?, IRealtimeClient>>? Realtime { get; set; }
    public List<Func<IImageGenerator, IServiceProvider?, IImageGenerator>>? ImageGeneration { get; set; }
    public List<Func<IEmbeddingGenerator, IServiceProvider?, IEmbeddingGenerator>>? Embeddings { get; set; }
    public List<Func<IHostedFileClient, IServiceProvider?, IHostedFileClient>>? HostedFiles { get; set; }
    public List<Func<IVoiceActivityDetector, HPD.Agent.Providers.ProviderComponentLifetimeContext, IServiceProvider?, IVoiceActivityDetector>>? VoiceActivityDetection { get; set; }
    public List<Func<IEotDetector, HPD.Agent.Providers.ProviderComponentLifetimeContext, IServiceProvider?, IEotDetector>>? EndOfTurnDetection { get; set; }
}

/// <summary>
/// Provides centralized cloning and precedence-aware merging for provider client configuration.
/// </summary>
public static class ProviderClientConfigResolver
{
    /// <summary>Resolves agent and run layers for one client family.</summary>
    /// <param name="agentClients">Agent-default client configuration.</param>
    /// <param name="family">The family to resolve.</param>
    /// <param name="runClients">Optional per-run client configuration.</param>
    /// <returns>The merged family configuration, or <see langword="null"/> when empty.</returns>
    public static ProviderClientConfig? Resolve(
        AgentClientsConfig? agentClients,
        HPD.Agent.Providers.ProviderClientFamily family,
        AgentClientsConfig? runClients = null,
        IDictionary<string, AgentProviderProfile>? providerProfiles = null)
    {
        var agentFamily = agentClients?.GetFamilyConfig(family);

        var runFamily = runClients?.GetFamilyConfig(family);
        var agentProviderKey = agentFamily?.ProviderKey;
        var effectiveProviderKey = FirstNonEmpty(runFamily?.ProviderKey, agentProviderKey);
        var providerChanged = !string.IsNullOrWhiteSpace(runFamily?.ProviderKey) &&
            !string.IsNullOrWhiteSpace(agentProviderKey) &&
            !string.Equals(runFamily.ProviderKey, agentProviderKey, StringComparison.Ordinal);

        var profile = GetProfile(providerProfiles, effectiveProviderKey)?.GetFamilyConfig(family);
        var compatibleAgentFamily = providerChanged ? null : agentFamily;

        return Merge(profile, compatibleAgentFamily, runFamily);
    }

    /// <summary>Merges provider client configurations in ascending precedence order.</summary>
    /// <param name="configs">Configuration layers, from lowest to highest precedence.</param>
    /// <returns>The merged configuration, or <see langword="null"/> when empty.</returns>
    public static ProviderClientConfig? Merge(params ProviderClientConfig?[] configs)
    {
        ProviderClientConfig? result = null;

        foreach (var config in configs)
        {
            if (config == null)
                continue;

            result ??= CreateLike(
                configs.FirstOrDefault(static value => value is not null && value.GetType() != typeof(ProviderClientConfig))
                ?? config);
            Apply(result, config);
        }

        return IsEmpty(result) ? null : result;
    }

    /// <summary>Clones a provider client configuration while preserving its family type.</summary>
    /// <param name="config">The configuration to clone.</param>
    /// <returns>An independent configuration instance.</returns>
    public static ProviderClientConfig Clone(ProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var clone = CreateLike(config);
        Apply(clone, config);
        return clone;
    }

    private static ProviderClientConfig CreateLike(ProviderClientConfig config) => config switch
    {
        ChatClientConfig => new ChatClientConfig(),
        RealtimeClientConfig => new RealtimeClientConfig(),
        ImageGenerationClientConfig => new ImageGenerationClientConfig(),
        EmbeddingsClientConfig => new EmbeddingsClientConfig(),
        TextToSpeechClientConfig => new TextToSpeechClientConfig(),
        SpeechToTextClientConfig => new SpeechToTextClientConfig(),
        HostedFilesClientConfig => new HostedFilesClientConfig(),
        VoiceActivityDetectionClientConfig => new VoiceActivityDetectionClientConfig(),
        EndOfTurnDetectionClientConfig => new EndOfTurnDetectionClientConfig(),
        _ => new ProviderClientConfig()
    };

    private static AgentProviderProfile? GetProfile(
        IDictionary<string, AgentProviderProfile>? profiles,
        string? providerKey)
    {
        if (profiles is null || string.IsNullOrWhiteSpace(providerKey))
            return null;

        return profiles.TryGetValue(providerKey, out var exact)
            ? exact
            : profiles.FirstOrDefault(pair =>
                string.Equals(pair.Key, providerKey, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static void Apply(ProviderClientConfig target, ProviderClientConfig source)
    {
        if (!string.IsNullOrWhiteSpace(source.ProviderKey))
            target.ProviderKey = source.ProviderKey;

        if (!string.IsNullOrWhiteSpace(source.ModelName))
            target.ModelName = source.ModelName;

        target.ApiKey = source.ApiKey ?? target.ApiKey;
        target.AuthenticationKey = source.AuthenticationKey ?? target.AuthenticationKey;
        target.Endpoint = source.Endpoint ?? target.Endpoint;
        if (target is ChatClientConfig targetChat && source is ChatClientConfig sourceChat)
        {
            targetChat.Temperature = sourceChat.Temperature ?? targetChat.Temperature;
            targetChat.TopP = sourceChat.TopP ?? targetChat.TopP;
            targetChat.TopK = sourceChat.TopK ?? targetChat.TopK;
            targetChat.MaxOutputTokens = sourceChat.MaxOutputTokens ?? targetChat.MaxOutputTokens;
            targetChat.FrequencyPenalty = sourceChat.FrequencyPenalty ?? targetChat.FrequencyPenalty;
            targetChat.PresencePenalty = sourceChat.PresencePenalty ?? targetChat.PresencePenalty;
            targetChat.Seed = sourceChat.Seed ?? targetChat.Seed;
            targetChat.StopSequences = sourceChat.StopSequences ?? targetChat.StopSequences;
            targetChat.Reasoning = sourceChat.Reasoning ?? targetChat.Reasoning;
            targetChat.ProviderOptions = sourceChat.ProviderOptions ?? targetChat.ProviderOptions;
            targetChat.Override = sourceChat.Override ?? targetChat.Override;
        }
        else if (target is RealtimeClientConfig targetRealtime && source is RealtimeClientConfig sourceRealtime)
        {
            targetRealtime.OutputAudioFormat = sourceRealtime.OutputAudioFormat ?? targetRealtime.OutputAudioFormat;
            targetRealtime.Voice = sourceRealtime.Voice ?? targetRealtime.Voice;
            targetRealtime.MaxOutputTokens = sourceRealtime.MaxOutputTokens ?? targetRealtime.MaxOutputTokens;
            targetRealtime.OutputModalities = sourceRealtime.OutputModalities ?? targetRealtime.OutputModalities;
            targetRealtime.Transcription = sourceRealtime.Transcription ?? targetRealtime.Transcription;
            targetRealtime.ProviderOptions = sourceRealtime.ProviderOptions ?? targetRealtime.ProviderOptions;
            targetRealtime.Override = sourceRealtime.Override ?? targetRealtime.Override;
        }
        else if (target is ImageGenerationClientConfig targetImage && source is ImageGenerationClientConfig sourceImage)
        {
            targetImage.Count = sourceImage.Count ?? targetImage.Count;
            targetImage.ImageSize = sourceImage.ImageSize ?? targetImage.ImageSize;
            targetImage.MediaType = sourceImage.MediaType ?? targetImage.MediaType;
            targetImage.StreamingCount = sourceImage.StreamingCount ?? targetImage.StreamingCount;
            targetImage.ProviderOptions = sourceImage.ProviderOptions ?? targetImage.ProviderOptions;
            targetImage.Override = sourceImage.Override ?? targetImage.Override;
        }
        else if (target is EmbeddingsClientConfig targetEmbeddings && source is EmbeddingsClientConfig sourceEmbeddings)
        {
            targetEmbeddings.Dimensions = sourceEmbeddings.Dimensions ?? targetEmbeddings.Dimensions;
            targetEmbeddings.ProviderOptions = sourceEmbeddings.ProviderOptions ?? targetEmbeddings.ProviderOptions;
            targetEmbeddings.Override = sourceEmbeddings.Override ?? targetEmbeddings.Override;
        }
        else if (target is TextToSpeechClientConfig targetTts && source is TextToSpeechClientConfig sourceTts)
        {
            targetTts.VoiceId = sourceTts.VoiceId ?? targetTts.VoiceId;
            targetTts.Language = sourceTts.Language ?? targetTts.Language;
            targetTts.AudioFormat = sourceTts.AudioFormat ?? targetTts.AudioFormat;
            targetTts.Speed = sourceTts.Speed ?? targetTts.Speed;
            targetTts.Pitch = sourceTts.Pitch ?? targetTts.Pitch;
            targetTts.Volume = sourceTts.Volume ?? targetTts.Volume;
            targetTts.ProviderOptions = sourceTts.ProviderOptions ?? targetTts.ProviderOptions;
            targetTts.Override = sourceTts.Override ?? targetTts.Override;
        }
        else if (target is SpeechToTextClientConfig targetStt && source is SpeechToTextClientConfig sourceStt)
        {
            targetStt.SpeechLanguage = sourceStt.SpeechLanguage ?? targetStt.SpeechLanguage;
            targetStt.SpeechSampleRate = sourceStt.SpeechSampleRate ?? targetStt.SpeechSampleRate;
            targetStt.TextLanguage = sourceStt.TextLanguage ?? targetStt.TextLanguage;
            targetStt.ProviderOptions = sourceStt.ProviderOptions ?? targetStt.ProviderOptions;
            targetStt.Override = sourceStt.Override ?? targetStt.Override;
        }
        else if (target is HostedFilesClientConfig targetFiles && source is HostedFilesClientConfig sourceFiles)
        {
            targetFiles.Scope = sourceFiles.Scope ?? targetFiles.Scope;
            targetFiles.Purpose = sourceFiles.Purpose ?? targetFiles.Purpose;
            targetFiles.Limit = sourceFiles.Limit ?? targetFiles.Limit;
            targetFiles.ProviderOptions = sourceFiles.ProviderOptions ?? targetFiles.ProviderOptions;
            targetFiles.Override = sourceFiles.Override ?? targetFiles.Override;
        }
        else if (target is VoiceActivityDetectionClientConfig targetVad && source is VoiceActivityDetectionClientConfig sourceVad)
            targetVad.OverrideFactory = sourceVad.OverrideFactory ?? targetVad.OverrideFactory;
        else if (target is EndOfTurnDetectionClientConfig targetEot && source is EndOfTurnDetectionClientConfig sourceEot)
            targetEot.OverrideFactory = sourceEot.OverrideFactory ?? targetEot.OverrideFactory;
        target.HttpReferer = source.HttpReferer ?? target.HttpReferer;
        target.AppName = source.AppName ?? target.AppName;
        target.PromptFormatter = source.PromptFormatter ?? target.PromptFormatter;
        target.ProviderConfig = source.ProviderConfig ?? target.ProviderConfig;

        if (source.CustomHeaders != null)
        {
            target.CustomHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source.CustomHeaders)
                target.CustomHeaders[pair.Key] = pair.Value;
        }

        target.SetConstructionOptionsRawJson(MergeConstructionOptions(
            target.GetConstructionOptionsRawJson(),
            source.GetConstructionOptionsRawJson()));
    }

    private static string? MergeConstructionOptions(string? lowerPriority, string? higherPriority)
    {
        if (string.IsNullOrWhiteSpace(lowerPriority))
            return higherPriority;

        if (string.IsNullOrWhiteSpace(higherPriority))
            return lowerPriority;

        var lower = ParseObject(lowerPriority);
        var higher = ParseObject(higherPriority);

        foreach (var pair in higher)
        {
            lower[pair.Key] = pair.Value?.DeepClone();
        }

        return lower.ToJsonString();
    }

    private static JsonElement? MergeConstructionOptions(JsonElement? lowerPriority, JsonElement? higherPriority)
    {
        var merged = MergeConstructionOptions(
            lowerPriority?.GetRawText(),
            higherPriority?.GetRawText());

        return string.IsNullOrWhiteSpace(merged)
            ? null
            : JsonDocument.Parse(merged).RootElement.Clone();
    }

    private static JsonObject ParseObject(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj)
            throw new InvalidOperationException("ConstructionOptions merge requires each non-empty value to be a JSON object.");

        return obj;
    }

    private static bool IsEmpty(ProviderClientConfig? config) =>
        config == null ||
        (string.IsNullOrWhiteSpace(config.ProviderKey) &&
         string.IsNullOrWhiteSpace(config.ModelName) &&
         string.IsNullOrWhiteSpace(config.ApiKey) &&
         string.IsNullOrWhiteSpace(config.AuthenticationKey) &&
         string.IsNullOrWhiteSpace(config.Endpoint) &&
         (config is not ChatClientConfig chat ||
          (chat.Temperature is null && chat.TopP is null && chat.TopK is null &&
           chat.MaxOutputTokens is null && chat.FrequencyPenalty is null &&
           chat.PresencePenalty is null && chat.Seed is null &&
           chat.StopSequences is null && chat.Reasoning is null &&
           chat.ProviderOptions is null && chat.Override is null)) &&
         config.CustomHeaders == null &&
         config.ConstructionOptions is null &&
         string.IsNullOrWhiteSpace(config.HttpReferer) &&
         string.IsNullOrWhiteSpace(config.AppName) &&
         config.PromptFormatter == null);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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
    /// Optional timeout for a single function execution.
    /// The default is <c>null</c>, which allows functions to run until the caller cancels them.
    /// Set an explicit value to enable function timeout middleware.
    /// </summary>
    public TimeSpan? SingleFunctionTimeout { get; set; }

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

/// <summary>Configuration for canonical thread compaction.</summary>
public class CompactionConfig
{
    public AutomaticCompactionPolicy? Automatic { get; set; }
    public CompactionSpecification? ForkCompaction { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TurnCountCompactionTrigger), "turnCount")]
[JsonDerivedType(typeof(InputTokenCompactionTrigger), "inputTokens")]
[JsonDerivedType(typeof(ContextPercentageCompactionTrigger), "contextPercentage")]
public abstract record CompactionTrigger;

public sealed record TurnCountCompactionTrigger(int Turns) : CompactionTrigger;

public sealed record InputTokenCompactionTrigger(long InputTokens) : CompactionTrigger;

public sealed record ContextPercentageCompactionTrigger(
    long TotalInputTokens,
    double Percentage) : CompactionTrigger;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CompactAtCurrentHead), "currentHead")]
[JsonDerivedType(typeof(CompactAtMessage), "message")]
[JsonDerivedType(typeof(CompactAtTurn), "turn")]
public abstract record CompactionPoint;

public sealed record CompactAtCurrentHead : CompactionPoint;

public sealed record CompactAtMessage(
    string MessageId,
    long? ExpectedJournalGeneration = null) : CompactionPoint;

public sealed record CompactAtTurn(
    string TurnId,
    long? ExpectedJournalGeneration = null) : CompactionPoint;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PreserveNoPreviousHistory), "none")]
[JsonDerivedType(typeof(PreservePreviousTurns), "previousTurns")]
[JsonDerivedType(typeof(PreservePreviousUserMessages), "previousUserMessages")]
public abstract record CompactionPreservation;

public sealed record PreserveNoPreviousHistory : CompactionPreservation;

public sealed record PreservePreviousTurns(int Count) : CompactionPreservation;

public sealed record PreservePreviousUserMessages(
    PreviousHistoryLimit Limit) : CompactionPreservation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PreviousItemCountLimit), "count")]
[JsonDerivedType(typeof(PreviousTokenBudgetLimit), "tokenBudget")]
public abstract record PreviousHistoryLimit;

public sealed record PreviousItemCountLimit(int Count) : PreviousHistoryLimit;

public sealed record PreviousTokenBudgetLimit(long Tokens) : PreviousHistoryLimit;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RemovalCompaction), "removal")]
[JsonDerivedType(typeof(SummarizingCompaction), "summarizing")]
public abstract record CompactionStrategy;

public sealed record RemovalCompaction : CompactionStrategy;

public sealed record SummarizingCompaction : CompactionStrategy
{
    public ProviderClientConfig? Provider { get; init; }
    public string? Instructions { get; init; }
}

public enum CompactionCommitMode
{
    Soft,
    Hard
}

public sealed record CompactionSpecification
{
    public required CompactionPoint Point { get; init; }
    public CompactionPreservation Preservation { get; init; } = new PreserveNoPreviousHistory();
    public required CompactionStrategy Strategy { get; init; }
    public CompactionCommitMode CommitMode { get; init; } = CompactionCommitMode.Soft;
}

public sealed record AutomaticCompactionPolicy
{
    public required CompactionTrigger Trigger { get; init; }
    public required CompactionSpecification Compaction { get; init; }
    public CompactionContinuation Continuation { get; init; } = CompactionContinuation.Continue;
}

/// <summary>Per-run automatic compaction override. Null disables it for the run.</summary>
public sealed record CompactionRunPolicy
{
    public AutomaticCompactionPolicy? Automatic { get; init; }
}

/// <summary>Options for an explicit thread compaction command.</summary>
public sealed record ThreadCompactionRequest
{
    public required CompactionSpecification Compaction { get; init; }
    public CompactionContinuation Continuation { get; init; } = CompactionContinuation.StopAfterCompaction;
}

/// <summary>
/// Behavior when compaction is triggered.
/// Controls whether the agent continues immediately or stops for user confirmation.
/// </summary>
public enum CompactionContinuation
{
    /// <summary>
    /// Continue immediately after compaction (default).
    /// Compaction happens transparently without interrupting the agent flow.
    /// Use when: Compaction is an implementation detail, users don't need to know.
    /// </summary>
    Continue,

    /// <summary>
    /// Stop execution after compaction without treating the stop as an error or user confirmation gate.
    /// Use for explicit compaction operations that should compact existing history and then end the run
    /// before a model call.
    /// </summary>
    StopAfterCompaction
}




/// <summary>
/// Configuration for agentic loop safety controls to prevent runaway execution.
/// </summary>
public class AgenticLoopConfig
{
    /// <summary>
    /// Maximum duration for a single turn before timeout. Null means no deadline.
    /// </summary>
    public TimeSpan? MaxTurnDuration { get; set; }

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
/// Configuration for Collapsing feature.
/// Controls hierarchical organization of functions to reduce token usage.
/// </summary>
public class CollapsingConfig
{
    /// <summary>
    /// Enable Collapsing for C# ToolHarnesses. When true, ToolHarness functions are hidden behind container functions.
    /// Default: true (enabled - ToolHarnesses with [Collapse] attribute are collapsed).
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
    /// List of toolharness names that should never be collapsed, even if they have containers.
    /// This is a runtime override that works even if the toolharness was compiled with collapse support.
    /// Use this to force specific toolharnesses to always show their functions directly.
    /// Example: new HashSet&lt;string&gt; { "MathToolHarness", "CoreTools" }
    /// </summary>
    public HashSet<string> NeverCollapse { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
/// Serializable agent-level defaults for HPD-owned audio behavior.
/// </summary>
public sealed class AudioConfig
{
    public bool Enabled { get; set; } = true;

    public AudioInputMode InputMode { get; set; } = AudioInputMode.Auto;

    public AudioOutputMode OutputMode { get; set; } = AudioOutputMode.Auto;

    public AudioPolicySet? Policy { get; set; }

    public AssistantOutputSynthesisMode AssistantOutputMode { get; set; } =
        AssistantOutputSynthesisMode.Disabled;

    public TextToSpeechPacingOptions? Pacing { get; set; }

    public ProgressiveTextToSpeechRouteMode ProgressiveRouteMode { get; set; } =
        ProgressiveTextToSpeechRouteMode.Auto;

    public PushTextInputAggregationMode PushTextAggregationMode { get; set; } =
        PushTextInputAggregationMode.ProviderDefault;

    public AssistantAudioArtifactCapturePolicy ArtifactCapturePolicy { get; set; } =
        AssistantAudioArtifactCapturePolicy.ContentStoreArtifact;

    public bool EnablePlayback { get; set; }
}

public enum AudioInputMode
{
    Auto = 0,
    None = 1,
    BatchSpeechToText = 2,
    ProviderRealtime = 3,
    ReferenceOnly = 4,
    Reject = 5
}

public enum AudioOutputMode
{
    Auto = 0,
    None = 1,
    TextOnly = 2,
    TextToSpeech = 3,
    ProviderRealtimeAudio = 4
}

#endregion

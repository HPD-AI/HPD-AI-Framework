// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Security;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;
using HPD.Agent.StructuredOutput;

namespace HPD.Agent;

/// <summary>
/// Selects the model transport used for an agent run.
/// </summary>
public enum AgentModelTransportMode
{
    /// <summary>
    /// Use the agent's default model transport.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Use the normal chat client model turn.
    /// </summary>
    Chat = 1,

    /// <summary>
    /// Use the native realtime client model turn.
    /// </summary>
    Realtime = 2
}

/// <summary>Controls approval middleware for an agent run.</summary>
public enum AgentApprovalPolicy
{
    /// <summary>Request approval for operations classified as protected.</summary>
    ReviewProtectedActions = 0,

    /// <summary>Approve protected operations without prompting.</summary>
    AutoApprove = 1
}

/// <summary>Controls host isolation for agent-initiated operations.</summary>
public enum AgentSandboxPolicy
{
    /// <summary>Enforce the host sandbox and its effective capability grants.</summary>
    Enforced = 0,

    /// <summary>Run without host sandbox isolation.</summary>
    Disabled = 1
}

/// <summary>Controls attempts to exceed the active sandbox grants.</summary>
public enum AgentSandboxEscapePolicy
{
    /// <summary>Request a user decision for a narrow additional capability.</summary>
    Ask = 0,

    /// <summary>Deny capabilities outside the active grants.</summary>
    Deny = 1
}

/// <summary>
/// Independent security controls applied to one agent run.
/// </summary>
public sealed record AgentSecurityProfile
{
    /// <summary>Gets the protected-operation approval policy.</summary>
    public AgentApprovalPolicy Approval { get; init; } = AgentApprovalPolicy.ReviewProtectedActions;

    /// <summary>Gets the host sandbox policy.</summary>
    public AgentSandboxPolicy Sandbox { get; init; } = AgentSandboxPolicy.Enforced;

    /// <summary>Gets the behavior for capabilities outside enforced sandbox grants.</summary>
    public AgentSandboxEscapePolicy SandboxEscape { get; init; } = AgentSandboxEscapePolicy.Ask;
}

/// <summary>
/// Per-invocation options for agent runs.
/// Enables runtime customization without mutating agent configuration.
/// FFI-serializable (JSON primitives only for serializable properties).
/// </summary>
/// <remarks>
/// <para>
/// <b>Design Philosophy:</b>
/// AgentRunConfig provides per-invocation customization that doesn't require rebuilding the agent.
/// This enables scenarios like:
/// - Runtime provider switching (OpenAI → Claude → local)
/// - Per-request temperature/token adjustments
/// - Multi-tenant SaaS with different contexts per user
/// - A/B testing with different configurations
/// </para>
/// <para>
/// <b>Priority Rules:</b>
/// - OverrideChatClient > ProviderKey/ModelId > Agent's default client
/// - SystemInstructions > Config.SystemInstructions (complete replacement)
/// - AdditionalSystemInstructions appends to resolved instructions
/// - ContextInstances > Builder-time contexts > Default context
/// </para>
/// </remarks>
public class AgentRunConfig
{
    /// <summary>
    /// Security controls for this run.
    /// </summary>
    public AgentSecurityProfile Security { get; set; } = new();

    /// <summary>Capabilities granted to the enforced sandbox for this run.</summary>
    public AgentSandboxConfiguration Sandbox { get; set; } = new();

    /// <summary>
    /// Chat parameters (temperature, tokens, etc.)
    /// JSON-serializable, no Microsoft.Extensions.AI dependency.
    /// </summary>
    public ChatRunConfig? Chat { get; set; }

    /// <summary>
    /// Model transport to use for the agent turn.
    /// </summary>
    public AgentModelTransportMode ModelTransport { get; set; } = AgentModelTransportMode.Auto;

    /// <summary>
    /// Provider-created client-family overrides for this run.
    /// </summary>
    public AgentClientConfig? Clients { get; set; }

    /// <summary>
    /// Provider key to switch to (e.g., "openai", "anthropic", "ollama").
    /// Works with ModelId to create the client via provider registry.
    /// Useful for simple provider switching without manual client creation.
    /// </summary>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// Model ID to use for the switched provider (e.g., "gpt-4", "claude-opus").
    /// If ProviderKey is not set, uses the provider from AgentConfig.
    /// If null with ProviderKey, uses the model from AgentConfig.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// API key to use when switching providers.
    /// Required when switching to a different provider that needs authentication.
    /// If null and switching to same provider, inherits from agent config.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Endpoint URL override for the provider.
    /// Useful for custom/self-hosted endpoints (e.g., local Ollama, Azure OpenAI).
    /// </summary>
    public string? ProviderEndpoint { get; set; }

    /// <summary>
    /// Custom HTTP headers to include in provider requests.
    /// Used for OAuth flows that require additional headers (e.g., ChatGPT-Account-Id for OpenAI Codex).
    /// These headers are merged with any provider-default headers.
    /// </summary>
    public Dictionary<string, string>? CustomHeaders { get; set; }

    /// <summary>
    /// Provider-specific options to use when switching providers for this run.
    /// Prefer this object-shaped property for JSON/YAML config.
    /// </summary>
    public JsonElement? ProviderOptions { get; set; }

    public string? GetProviderOptionsRawJson()
        => ProviderOptions?.GetRawText();

    internal ClientProviderConfig? GetChatProviderOverride()
    {
        if (string.IsNullOrWhiteSpace(ProviderKey) &&
            string.IsNullOrWhiteSpace(ModelId) &&
            string.IsNullOrWhiteSpace(ApiKey) &&
            string.IsNullOrWhiteSpace(ProviderEndpoint) &&
            CustomHeaders == null &&
            ProviderOptions is null)
            return null;

        return new ClientProviderConfig
        {
            ProviderKey = ProviderKey ?? string.Empty,
            ModelName = ModelId ?? string.Empty,
            ApiKey = ApiKey,
            Endpoint = ProviderEndpoint,
            CustomHeaders = CustomHeaders,
            ProviderOptions = ProviderOptions
        };
    }

    /// <summary>
    /// Override the chat client for this specific run.
    /// Highest priority - used if provided, overriding ProviderKey/ModelId.
    /// Enables dynamic provider switching without rebuilding.
    /// Not JSON-serializable (for direct C# usage).
    /// </summary>
    [JsonIgnore]
    public IChatClient? OverrideChatClient { get; set; }

    /// <summary>
    /// Override the realtime client for this specific run.
    /// Highest priority when <see cref="ModelTransport"/> resolves to realtime.
    /// </summary>
    [JsonIgnore]
    public IRealtimeClient? OverrideRealtimeClient { get; set; }

    /// <summary>
    /// Realtime input transcription options for native realtime turns.
    /// When set, providers that support realtime transcription can emit readable user transcripts.
    /// </summary>
    [JsonIgnore]
    public TranscriptionOptions? RealtimeTranscriptionOptions { get; set; }

    [JsonIgnore]
    public IImageGenerator? OverrideImageGenerator { get; set; }

    [JsonIgnore]
    public IEmbeddingGenerator? OverrideEmbeddingGenerator { get; set; }

    [JsonIgnore]
    public IHostedFileClient? OverrideHostedFileClient { get; set; }

    [JsonIgnore]
    public Func<Providers.ProviderComponentLifetimeContext, IVoiceActivityDetector>? OverrideVoiceActivityDetectorFactory { get; set; }

    [JsonIgnore]
    public Func<Providers.ProviderComponentLifetimeContext, IEotDetector>? OverrideEndOfTurnDetectorFactory { get; set; }

    /// <summary>
    /// System instructions to use for this run (completely replaces configured instructions).
    /// Useful for completely different personas or behaviors.
    /// Example: "You are a strict code reviewer" vs "You are a brainstorming partner"
    /// If both this and AdditionalSystemInstructions are set, both are used.
    /// </summary>
    public string? SystemInstructions { get; set; }

    /// <summary>
    /// Additional system instructions to append to the base instructions.
    /// Useful for one-off adjustments without replacing base instructions.
    /// Example: Base="helpful assistant" + Additional="For this request, prioritize security"
    /// If SystemInstructions is set, this appends to that instead of base config.
    /// </summary>
    public string? AdditionalSystemInstructions { get; set; }

    /// <summary>
    /// Context values to inject or override for this run.
    /// Available to middleware via AgentMiddlewareContext.Properties.
    /// Useful for request-specific data: user ID, tenant ID, request metadata, etc.
    /// </summary>
    public Dictionary<string, object>? ContextOverrides { get; set; }

    /// <summary>
    /// Timeout for the entire run (overrides config).
    /// Useful for varying timeout based on message complexity or user tier.
    /// Null = use config default.
    /// </summary>
    public TimeSpan? RunTimeout { get; set; }

    /// <summary>
    /// Whether to use cached responses for this run.
    /// Null = use config default, true = always cache, false = skip cache.
    /// Useful for dry-runs or when freshness is critical.
    /// </summary>
    public bool? UseCache { get; set; }

    /// <summary>
    /// Skip tool/function execution for this run (dry-run mode).
    /// Useful for testing agent planning without side effects.
    /// Agent will plan and call functions, but they won't execute.
    /// </summary>
    public bool SkipTools { get; set; } = false;

    /// <summary>
    /// When true, coalesces streaming deltas into single complete events.
    /// - Text: Multiple TextDeltaEvent("Hello"), TextDeltaEvent(" world") → Single TextDeltaEvent("Hello world")
    /// - Reasoning: Multiple ReasoningDeltaEvent chunks → Single ReasoningDeltaEvent with complete reasoning
    /// Reduces event count and simplifies processing at the cost of increased latency.
    /// When null, uses the agent's config default (AgentConfig.CoalesceDeltas).
    /// </summary>
    public bool? CoalesceDeltas { get; set; }

    /// <summary>
    /// Runtime middleware to inject only for this run.
    /// Applied as outer middleware for this run: before configured middleware
    /// for Before* hooks and after configured middleware for After* hooks.
    /// Not JSON-serializable (for direct C# usage).
    /// Useful for temporary observability, monitoring, or custom logic.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<IAgentMiddleware>? RuntimeMiddleware { get; set; }

    /// <summary>
    /// Permission requirement overrides for this specific run.
    /// Key = function/tool name, Value = whether permission is required.
    /// Overrides the generic <c>PermissionMiddleware</c> requirement temporarily.
    /// Command-specific permission middleware may apply additional policy.
    /// Ignored when <see cref="Security"/> uses <see cref="AgentApprovalPolicy.AutoApprove"/>.
    /// Unknown function names are ignored because overrides are only read when
    /// that function is invoked.
    /// Example: { "ReadFile": false, "ExecuteCommand": true }
    /// </summary>
    public Dictionary<string, bool>? PermissionOverrides { get; set; }

    /// <summary>
    /// Client tool configuration for this run.
    /// Allows dynamic ToolHarness/tool registration without rebuilding agent.
    /// </summary>
    public ClientTools.AgentClientInput? ClientToolInput { get; set; }

    /// <summary>
    /// Live client app providers to bind for this run.
    /// </summary>
    /// <remarks>
    /// These references do not define tools. They select connected providers whose manifests
    /// advertise client tool harnesses that can be exposed after a binding lease is created.
    /// </remarks>
    public List<ClientTools.ClientAppProviderReference>? ClientAppProviders { get; set; }

    /// <summary>
    /// Conversation ID override (for multi-tenant scenarios or threading).
    /// Null = use thread's conversation ID.
    /// </summary>
    public string? ConversationIdOverride { get; set; }

    /// <summary>
    /// Custom streaming callback (for native bindings).
    /// Not JSON-serializable.
    /// Allows native code to handle streaming updates differently.
    /// </summary>
    [JsonIgnore]
    public Func<AgentEvent, Task>? CustomStreamCallback { get; set; }

    /// <summary>
    /// Runtime context instances for tools (Runtime Context Injection).
    /// Maps tool name -> context instance (e.g., "SearchTools" -> ProviderContext instance).
    /// Enables dynamic, per-invocation context injection WITHOUT rebuilding the agent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Priority (for context resolution):</b>
    /// 1. Runtime context from ContextInstances (highest - per-invocation override)
    /// 2. Builder-time context from .WithTools&lt;T&gt;(context)
    /// 3. Default context from .WithDefaultMetadata(context)
    /// 4. Null (no context - templates unresolved, conditions skipped)
    /// </para>
    /// <para>
    /// <b>How It Works:</b>
    /// The source generator creates CreateTools() methods that accept context.
    /// Each tool's CreateFunctions(instance, context) is invoked with the selected context.
    /// This means descriptions are resolved, conditions evaluated, parameters filtered - all at runtime!
    /// </para>
    /// <para>
    /// <b>Use Cases:</b>
    /// - Multi-tenant SaaS: Different contexts per user/tenant
    /// - A/B Testing: Run variants with different contexts
    /// - Dynamic feature flags: Context can control function visibility
    /// - Request metadata: Inject tracing, user info, etc. per-call
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var options = new AgentRunConfig
    /// {
    ///     ContextInstances = new()
    ///     {
    ///         ["SearchTools"] = new ProviderContext { ProviderName = "OpenAI" },
    ///         ["DatabaseTools"] = new DbContext { TenantId = user.TenantId }
    ///     }
    /// };
    /// await agent.RunAsync("Search for tenant-specific records.", runConfig: options);
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public Dictionary<string, IToolMetadata>? ContextInstances { get; set; }

    #region Background Responses

    /// <summary>
    /// Allow the provider to run the operation in background mode.
    /// When true, operation may return immediately with a ContinuationToken.
    /// When false, operation blocks until complete (traditional behavior).
    /// When null, uses default from AgentConfig.BackgroundResponses.DefaultAllow.
    /// Provider-dependent: Unsupporting providers will ignore this and behave synchronously.
    /// </summary>
    public bool? AllowBackgroundResponses { get; set; }

    /// <summary>
    /// Continuation token for polling/resuming a background operation.
    /// For polling: Set this to the token from a previous response to poll for completion.
    /// For streaming resumption: Set this to resume streaming from where it was interrupted.
    /// Uses Microsoft.Extensions.AI.ResponseContinuationToken type directly.
    /// </summary>
    [JsonIgnore]
    public ResponseContinuationToken? ContinuationToken { get; set; }

    /// <summary>
    /// Override polling interval for this specific run.
    /// Null = use config default (BackgroundResponsesConfig.DefaultPollingInterval).
    /// </summary>
    public TimeSpan? BackgroundPollingInterval { get; set; }

    /// <summary>
    /// Override timeout for this specific run.
    /// Null = use config default (BackgroundResponsesConfig.DefaultTimeout).
    /// </summary>
    public TimeSpan? BackgroundTimeout { get; set; }

    #endregion

    #region Content Attachments

    /// <summary>
    /// User message text for this run.
    /// Combined with Attachments to form the user ChatMessage.
    /// If only Attachments are provided (no UserMessage), runtime integrations handle
    /// the content transformation (for example, audio may be transcribed before the model call).
    /// </summary>
    public string? UserMessage { get; set; }

    /// <summary>
    /// Binary content attachments (images, audio, documents, video) for this run.
    /// Use typed classes: ImageContent, AudioContent, DocumentContent, VideoContent.
    /// Combined with UserMessage to form the user ChatMessage.
    /// Middleware processes each content type appropriately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attachments can be sent without a UserMessage. For example:
    /// - Audio-only: Audio runtime integration transcribes → becomes the message
    /// - Image-only: Sent to vision model for description
    /// - Document-only: DocumentHandlingMiddleware extracts text
    /// </para>
    /// <para>
    /// <b>Type Constraint:</b> IReadOnlyList&lt;DataContent&gt; (not AIContent) ensures only binary
    /// content can be attached. TextContent must go in UserMessage string, not as attachments.
    /// This provides clear semantics: text vs. binary content separation.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var options = new AgentRunConfig
    /// {
    ///     UserMessage = "Analyze this document",
    ///     Attachments = [await DocumentContent.FromFileAsync("report.pdf")]
    /// };
    /// await agent.RunAsync(options);
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore]  // DataContent derivatives not JSON-serializable
    public IReadOnlyList<DataContent>? Attachments { get; set; }

    /// <summary>
    /// Controls how DataContent attachments are uploaded: via provider-native HostedFileClient,
    /// framework IContentStore, or Auto (prefer hosted if available).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>UploadStrategy.Auto (default):</b>
    /// Intelligently routes uploads to HostedFileClient if the current provider supports it,
    /// otherwise falls back to IContentStore. Provides best-of-both-worlds behavior.
    /// </para>
    /// <para>
    /// <b>UploadStrategy.Hosted:</b>
    /// Forces upload through provider's HostedFileClient.
    /// Throws InvalidOperationException if provider doesn't support it.
    /// </para>
    /// <para>
    /// <b>UploadStrategy.Local:</b>
    /// Forces upload to IContentStore (local/framework-managed).
    /// Ignores provider capabilities.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// // Use Auto (default) — most flexible
    /// await agent.RunAsync("analyze this", attachments: images);
    ///
    /// // Force OpenAI's native Files API
    /// await agent.RunAsync("analyze", attachments: images,
    ///     runConfig: new AgentRunConfig { UploadStrategy = UploadStrategy.Hosted });
    ///
    /// // Force local storage for ephemeral sessions
    /// await agent.RunAsync("analyze", attachments: images,
    ///     runConfig: new AgentRunConfig { UploadStrategy = UploadStrategy.Local });
    /// </code>
    /// </para>
    /// </remarks>
    public UploadStrategy UploadStrategy { get; set; } = UploadStrategy.Auto;

    #endregion

    #region Audio

    /// <summary>
    /// Per-run overrides for HPD-owned audio behavior. Provider switching stays in
    /// <see cref="Clients"/> using the SpeechToText, TextToSpeech, and Realtime families.
    /// </summary>
    public AudioRunConfig? Audio { get; set; }

    #endregion

    #region Compaction

    /// <summary>
    /// Per-run compaction policy. Null means use the agent's configured compaction defaults.
    /// </summary>
    public CompactionRunPolicy? Compaction { get; set; }

    #endregion

    #region Structured Output

    /// <summary>
    /// Configuration for structured output mode.
    /// When set, enables RunStructuredAsync&lt;T&gt;() to emit typed response events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Structured output allows agents to return strongly-typed responses instead of
    /// free-form text. Two modes are supported:
    /// </para>
    /// <list type="bullet">
    /// <item><b>native</b> (default): Uses provider's ResponseFormat with JSON schema. Supports streaming partials.</item>
    /// <item><b>tool</b>: Auto-generated output tool. Use when mixing structured output with regular tools.</item>
    /// </list>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var options = new AgentRunConfig
    /// {
    ///     StructuredOutput = new StructuredOutputOptions { Mode = "native" }
    /// };
    /// agent.On&lt;StructuredResultEvent&lt;Report&gt;&gt;(result =>
    /// {
    ///     Console.WriteLine(result.Value);
    /// });
    ///
    /// await agent.RunStructuredAsync&lt;Report&gt;("Generate the report.", runConfig: options);
    /// </code>
    /// </para>
    /// </remarks>
    public StructuredOutputOptions? StructuredOutput { get; set; }

    /// <summary>
    /// Additional tools to add for this run only.
    /// These are merged with the agent's configured tools during RunAsync.
    /// Useful for injecting dynamic tools like handoff functions in multi-agent workflows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Use Cases:</b>
    /// - Multi-agent handoffs: Inject handoff_to_X() tools dynamically
    /// - Per-request tools: Add user-specific or context-specific tools
    /// - Testing: Inject mock tools for testing agent behavior
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// var handoffTool = AIFunctionFactory.Create(() => "solver", "handoff_to_solver", "Route to math solver");
    /// var options = new AgentRunConfig
    /// {
    ///     AdditionalTools = new List&lt;AIFunction&gt; { handoffTool }
    /// };
    /// await agent.RunAsync("Route this to the right specialist.", runConfig: options);
    /// </code>
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<AIFunction>? AdditionalTools { get; set; }

    /// <summary>
    /// Tool mode override for this run only.
    /// When set, overrides the agent's configured ToolMode.
    /// </summary>
    /// <remarks>
    /// Common values:
    /// - <c>ChatToolMode.Auto</c>: Model decides whether to use tools
    /// - <c>ChatToolMode.RequireAny</c>: Model must call at least one tool
    /// - <c>ChatToolMode.RequireTool("name")</c>: Model must call specific tool
    /// </remarks>
    [JsonIgnore]
    public ChatToolMode? ToolModeOverride { get; set; }

    /// <summary>
    /// Runtime tools to add for this run only.
    /// Used internally by structured output tool mode.
    /// These are merged with the agent's configured tools during RunAsync.
    /// </summary>
    [JsonIgnore]
    internal List<AITool>? RuntimeTools { get; set; }

    /// <summary>
    /// Tool mode override for this run only.
    /// Used internally by structured output tool/union modes to force tool calling.
    /// When set, overrides the agent's configured ToolMode.
    /// </summary>
    [JsonIgnore]
    internal ChatToolMode? RuntimeToolMode { get; set; }

    #endregion

    #region Evaluation

    /// <summary>
    /// When true, EvaluationMiddleware skips all evaluation for this run.
    /// Set automatically by RunEvals on every internal agent run to prevent
    /// live evaluators from double-firing during batch evaluation.
    /// </summary>
    [JsonIgnore]
    public bool DisableEvaluators { get; set; } = false;

    /// <summary>
    /// When true, indicates this AgentRunConfig was created by EvaluationMiddleware
    /// to invoke a judge LLM. EvaluationMiddleware checks this flag first in
    /// AfterMessageTurnAsync and returns immediately if set, preventing eval loops.
    /// Only meaningful when the judge IChatClient is itself a wrapping Agent instance.
    /// </summary>
    [JsonIgnore]
    public bool IsInternalEvalJudgeCall { get; set; } = false;

    /// <summary>
    /// Evaluation-package owned per-run evaluator additions.
    /// Stored as object to keep HPD-Agent independent from HPD-Agent.Evaluations.
    /// Use HPD.Agent.Evaluations.Integration.AgentRunConfigEvalExtensions for
    /// the typed API.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<object>? AdditionalEvaluators { get; set; }

    /// <summary>
    /// Per-run sampling override for all registered evaluators.
    /// Null means use each evaluator's registration-time sampling rate.
    /// </summary>
    [JsonIgnore]
    public double? EvaluatorSamplingOverride { get; set; }

    /// <summary>
    /// Evaluation-package owned per-run judge configuration override.
    /// Stored as object to keep HPD-Agent independent from HPD-Agent.Evaluations.
    /// Use HPD.Agent.Evaluations.Integration.AgentRunConfigEvalExtensions for
    /// the typed API.
    /// </summary>
    [JsonIgnore]
    public object? EvalJudgeConfigOverride { get; set; }

    #endregion
}

public sealed class AudioRunConfig
{
    public bool? Enabled { get; set; }

    public AudioInputMode? InputMode { get; set; }

    public AudioOutputMode? OutputMode { get; set; }

    public AssistantOutputSynthesisMode? AssistantOutputMode { get; set; }

    public TextToSpeechPacingOptions? Pacing { get; set; }

    public ProgressiveTextToSpeechRouteMode? ProgressiveRouteMode { get; set; }

    public PushTextInputAggregationMode? PushTextAggregationMode { get; set; }

    public AssistantAudioArtifactCapturePolicy? ArtifactCapturePolicy { get; set; }

    public string? VoiceId { get; set; }

    public string? Language { get; set; }

    public string? OutputFormat { get; set; }

    public string? ContentType { get; set; }

    public float? Speed { get; set; }

    public bool? EnablePlayback { get; set; }
}

/// <summary>
/// Chat-specific run options (JSON-serializable).
/// Subset of Microsoft.Extensions.AI.ChatOptions with only JSON primitives.
/// FFI-friendly - no complex types, no dependencies.
/// </summary>
public class ChatRunConfig
{
    /// <summary>
    /// Creates a new instance of ChatRunConfig.
    /// </summary>
    public ChatRunConfig() { }

    /// <summary>
    /// Creates a new instance of ChatRunConfig from Microsoft.Extensions.AI.ChatOptions.
    /// </summary>
    /// <param name="options">The ChatOptions to convert from</param>
    public ChatRunConfig(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Temperature = options.Temperature.HasValue ? (double)options.Temperature.Value : null;
        TopP = options.TopP.HasValue ? (double)options.TopP.Value : null;
        TopK = options.TopK;
        MaxOutputTokens = options.MaxOutputTokens;
        FrequencyPenalty = options.FrequencyPenalty.HasValue ? (double)options.FrequencyPenalty.Value : null;
        PresencePenalty = options.PresencePenalty.HasValue ? (double)options.PresencePenalty.Value : null;
        Seed = options.Seed;
        ModelId = options.ModelId;
        StopSequences = options.StopSequences as IReadOnlyList<string>;
        Reasoning = ReasoningOptions.FromMicrosoftReasoningOptions(options.Reasoning);

        if (options.AdditionalProperties?.Count > 0)
        {
            AdditionalProperties = new Dictionary<string, object>();
            foreach (var kvp in options.AdditionalProperties)
            {
                AdditionalProperties[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Temperature (0.0-2.0). Higher = more creative, lower = more deterministic.
    /// Null = use config default.
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>
    /// Top-P (nucleus) sampling (0.0-1.0). Controls diversity of output.
    /// Null = use config default.
    /// </summary>
    [JsonPropertyName("topP")]
    public double? TopP { get; set; }

    /// <summary>
    /// Top-K sampling. The number of most probable tokens that the model considers when generating the next part of the text.
    /// This property reduces the probability of generating nonsense. A higher value gives more diverse answers, while a lower value is more conservative.
    /// Null = use config default.
    /// </summary>
    [JsonPropertyName("topK")]
    public int? TopK { get; set; }

    /// <summary>
    /// Maximum output tokens for this run.
    /// Null = use config default.
    /// </summary>
    [JsonPropertyName("maxOutputTokens")]
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Frequency penalty (-2.0 to 2.0). Reduces repetition.
    /// Null = use config default.
    /// </summary>
    [JsonPropertyName("frequencyPenalty")]
    public double? FrequencyPenalty { get; set; }

    /// <summary>
    /// Presence penalty (-2.0 to 2.0). Encourages new topics.
    /// Null = use config default.
    /// </summary>
    [JsonPropertyName("presencePenalty")]
    public double? PresencePenalty { get; set; }

    /// <summary>
    /// Random seed for deterministic generation where supported.
    /// Null = use config default.
    /// </summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>
    /// Model ID to use (e.g., "gpt-4-turbo").
    /// Note: Prefer using ProviderKey/ModelId in AgentRunConfig for provider switching.
    /// This is for fine-tuning within a provider.
    /// Null = use config default.
    /// </summary>
    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }

    /// <summary>
    /// Stop sequences that signal end of generation.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public IReadOnlyList<string>? StopSequences { get; set; }

    /// <summary>
    /// Provider-specific additional properties (for advanced use).
    /// </summary>
    [JsonPropertyName("additionalProperties")]
    public Dictionary<string, object>? AdditionalProperties { get; set; }

    /// <summary>
    /// Reasoning options for the chat request.
    /// Controls how much computational effort the model should put into reasoning
    /// and how that reasoning should be exposed.
    /// </summary>
    /// <remarks>
    /// Use <see cref="ReasoningEffort"/> values: None, Low, Medium, High, ExtraHigh.
    /// Use <see cref="ReasoningOutput"/> values: None, Summary, Full.
    /// </remarks>
    [JsonPropertyName("reasoning")]
    public ReasoningOptions? Reasoning { get; set; }

    /// <summary>
    /// Response format configuration for structured output.
    /// When set, instructs the provider to return JSON matching a schema.
    /// </summary>
    /// <remarks>
    /// For structured output, prefer using <see cref="AgentRunConfig.StructuredOutput"/>
    /// which is handled automatically by RunStructuredAsync&lt;T&gt;().
    /// </remarks>
    [JsonIgnore] // Not FFI-serializable (ChatResponseFormat contains complex types)
    public ChatResponseFormat? ResponseFormat { get; set; }

    /// <summary>
    /// Converts to Microsoft.Extensions.AI.ChatOptions for internal use.
    /// Returns null if no overrides are specified.
    /// </summary>
    public ChatOptions? ToMicrosoftChatOptions()
    {
        if (Temperature == null && TopP == null && TopK == null && MaxOutputTokens == null &&
            FrequencyPenalty == null && PresencePenalty == null &&
            Seed == null &&
            string.IsNullOrEmpty(ModelId) && StopSequences == null &&
            ResponseFormat == null && Reasoning == null &&
            (AdditionalProperties == null || AdditionalProperties.Count == 0))
        {
            return null;  // No overrides
        }

        var options = new ChatOptions();

        if (Temperature.HasValue)
            options.Temperature = (float)Temperature.Value;
        if (TopP.HasValue)
            options.TopP = (float)TopP.Value;
        if (TopK.HasValue)
            options.TopK = TopK.Value;
        if (MaxOutputTokens.HasValue)
            options.MaxOutputTokens = MaxOutputTokens.Value;
        if (FrequencyPenalty.HasValue)
            options.FrequencyPenalty = (float)FrequencyPenalty.Value;
        if (PresencePenalty.HasValue)
            options.PresencePenalty = (float)PresencePenalty.Value;
        if (Seed.HasValue)
            options.Seed = Seed.Value;
        if (!string.IsNullOrEmpty(ModelId))
            options.ModelId = ModelId;

        if (StopSequences?.Count > 0)
        {
            options.StopSequences = StopSequences.ToList();
        }

        if (ResponseFormat != null)
            options.ResponseFormat = ResponseFormat;

        if (Reasoning != null)
            options.Reasoning = Reasoning.ToMicrosoftReasoningOptions();

        if (AdditionalProperties?.Count > 0)
        {
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            foreach (var kvp in AdditionalProperties)
            {
                options.AdditionalProperties[kvp.Key] = kvp.Value;
            }
        }

        return options;
    }

    /// <summary>
    /// Merges these options with existing ChatOptions.
    /// This options instance takes precedence over base options.
    /// </summary>
    /// <param name="baseOptions">Base options to merge with (can be null)</param>
    /// <returns>Merged ChatOptions, or null if both are empty</returns>
    public ChatOptions? MergeWith(ChatOptions? baseOptions)
    {
        var thisOptions = ToMicrosoftChatOptions();

        if (thisOptions == null && baseOptions == null)
            return null;

        if (thisOptions == null)
            return baseOptions;

        if (baseOptions == null)
            return thisOptions;

        var merged = baseOptions.Clone();
        merged.Temperature = thisOptions.Temperature ?? baseOptions.Temperature;
        merged.TopP = thisOptions.TopP ?? baseOptions.TopP;
        merged.TopK = thisOptions.TopK ?? baseOptions.TopK;
        merged.MaxOutputTokens = thisOptions.MaxOutputTokens ?? baseOptions.MaxOutputTokens;
        merged.FrequencyPenalty = thisOptions.FrequencyPenalty ?? baseOptions.FrequencyPenalty;
        merged.PresencePenalty = thisOptions.PresencePenalty ?? baseOptions.PresencePenalty;
        merged.ModelId = thisOptions.ModelId ?? baseOptions.ModelId;
        merged.StopSequences = thisOptions.StopSequences ?? baseOptions.StopSequences;
        merged.Tools = baseOptions.Tools;  // Always from base (tools are agent-level)
        merged.ToolMode = baseOptions.ToolMode;
        merged.ResponseFormat = thisOptions.ResponseFormat ?? baseOptions.ResponseFormat;
        merged.Reasoning = thisOptions.Reasoning ?? baseOptions.Reasoning;
        merged.Seed = thisOptions.Seed ?? baseOptions.Seed;

        // Merge additional properties
        if (baseOptions.AdditionalProperties?.Count > 0 || thisOptions.AdditionalProperties?.Count > 0)
        {
            merged.AdditionalProperties = new AdditionalPropertiesDictionary();

            // Base first
            if (baseOptions.AdditionalProperties != null)
            {
                foreach (var kvp in baseOptions.AdditionalProperties)
                {
                    merged.AdditionalProperties[kvp.Key] = kvp.Value;
                }
            }

            // Override with this
            if (thisOptions.AdditionalProperties != null)
            {
                foreach (var kvp in thisOptions.AdditionalProperties)
                {
                    merged.AdditionalProperties[kvp.Key] = kvp.Value;
                }
            }
        }

        return merged;
    }
}

/// <summary>
/// Specifies the level of reasoning effort that should be applied when generating chat responses.
/// </summary>
/// <remarks>
/// This value suggests how much computational effort the model should put into reasoning.
/// Higher values may result in more thoughtful responses but with increased latency and token usage.
/// The specific interpretation and support for each level may vary between providers or even between models from the same provider.
/// </remarks>
public enum ReasoningEffort
{
    /// <summary>
    /// No reasoning effort.
    /// </summary>
    None = 0,

    /// <summary>
    /// Low reasoning effort. Minimal reasoning for faster responses.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium reasoning effort. Balanced reasoning for most use cases.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High reasoning effort. Extensive reasoning for complex tasks.
    /// </summary>
    High = 3,

    /// <summary>
    /// Extra high reasoning effort. Maximum reasoning for the most demanding tasks.
    /// </summary>
    ExtraHigh = 4,
}

/// <summary>
/// Specifies how reasoning content should be included in the response.
/// </summary>
/// <remarks>
/// Some providers support including reasoning or thinking traces in the response.
/// This setting controls whether and how that reasoning content is exposed.
/// </remarks>
public enum ReasoningOutput
{
    /// <summary>
    /// No reasoning output. Do not include reasoning content in the response.
    /// </summary>
    None = 0,

    /// <summary>
    /// Summary reasoning output. Include a summary of the reasoning process.
    /// </summary>
    Summary = 1,

    /// <summary>
    /// Full reasoning output. Include all reasoning content in the response.
    /// </summary>
    Full = 2,
}

/// <summary>
/// Represents options for configuring reasoning behavior in chat requests.
/// Wrapper around Microsoft.Extensions.AI.ReasoningOptions for FFI-friendly usage.
/// </summary>
public class ReasoningOptions
{
    /// <summary>
    /// Gets or sets the level of reasoning effort to apply.
    /// </summary>
    [JsonPropertyName("effort")]
    public ReasoningEffort? Effort { get; set; }

    /// <summary>
    /// Gets or sets how reasoning content should be included in the response.
    /// </summary>
    [JsonPropertyName("output")]
    public ReasoningOutput? Output { get; set; }

    /// <summary>
    /// Converts to Microsoft.Extensions.AI.ReasoningOptions for internal use.
    /// </summary>
    internal Microsoft.Extensions.AI.ReasoningOptions ToMicrosoftReasoningOptions()
    {
        return new Microsoft.Extensions.AI.ReasoningOptions
        {
            Effort = Effort.HasValue ? (Microsoft.Extensions.AI.ReasoningEffort)Effort.Value : null,
            Output = Output.HasValue ? (Microsoft.Extensions.AI.ReasoningOutput)Output.Value : null,
        };
    }

    /// <summary>
    /// Creates from Microsoft.Extensions.AI.ReasoningOptions.
    /// </summary>
    internal static ReasoningOptions? FromMicrosoftReasoningOptions(Microsoft.Extensions.AI.ReasoningOptions? options)
    {
        if (options == null)
            return null;

        return new ReasoningOptions
        {
            Effort = options.Effort.HasValue ? (ReasoningEffort)options.Effort.Value : null,
            Output = options.Output.HasValue ? (ReasoningOutput)options.Output.Value : null,
        };
    }

    /// <summary>
    /// Creates a shallow clone of this instance.
    /// </summary>
    public ReasoningOptions Clone() => new()
    {
        Effort = Effort,
        Output = Output,
    };
}

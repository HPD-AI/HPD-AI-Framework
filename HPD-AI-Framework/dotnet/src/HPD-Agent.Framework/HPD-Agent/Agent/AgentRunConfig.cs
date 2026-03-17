// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;
using HPD.Agent.StructuredOutput;

namespace HPD.Agent;

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
    /// Chat parameters (temperature, tokens, etc.)
    /// JSON-serializable, no Microsoft.Extensions.AI dependency.
    /// </summary>
    public ChatRunConfig? Chat { get; set; }

    /// <summary>
    /// Provider key to switch to (e.g., "openai", "anthropic", "ollama").
    /// Works with ModelId to create the client via provider registry.
    /// Useful for simple provider switching without manual client creation.
    /// </summary>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// Model ID to use for the switched provider (e.g., "gpt-4", "claude-opus").
    /// Ignored if ProviderKey is not set.
    /// If null with ProviderKey, uses default model from config.
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
    /// Override the chat client for this specific run.
    /// Highest priority - used if provided, overriding ProviderKey/ModelId.
    /// Enables dynamic provider switching without rebuilding.
    /// Not JSON-serializable (for direct C# usage).
    /// </summary>
    [JsonIgnore]
    public IChatClient? OverrideChatClient { get; set; }

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
    /// Applied AFTER agent's configured middleware.
    /// Not JSON-serializable (for direct C# usage).
    /// Useful for temporary observability, monitoring, or custom logic.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<IAgentMiddleware>? RuntimeMiddleware { get; set; }

    /// <summary>
    /// Permission overrides for this specific run.
    /// Key = permission name, Value = approved (true) or denied (false).
    /// Overrides agent's configured permission policies temporarily.
    /// Example: { "delete_files": false, "external_api_calls": true }
    /// </summary>
    public Dictionary<string, bool>? PermissionOverrides { get; set; }

    /// <summary>
    /// Client tool configuration for this run.
    /// Allows dynamic Toolkit/tool registration without rebuilding agent.
    /// </summary>
    public ClientTools.AgentClientInput? ClientToolInput { get; set; }

    /// <summary>
    /// Conversation ID override (for multi-tenant scenarios or branching).
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
    /// await agent.RunAsync(messages, options);
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
    /// If only Attachments are provided (no UserMessage), middleware handles
    /// the content transformation (e.g., AudioPipelineMiddleware transcribes audio).
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
    /// - Audio-only: AudioPipelineMiddleware transcribes → becomes the message
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

    #endregion

    #region Audio

    /// <summary>
    /// Audio configuration for this run.
    /// When set, enables voice input/output capabilities.
    /// Overrides AudioPipelineMiddleware defaults when set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Audio options control how voice is processed for this specific run:
    /// - Voice switching: Change TTS voice per-request
    /// - Provider switching: Switch TTS/STT providers dynamically
    /// - I/O mode: What input/output modalities to use
    /// - Language: Override language for multilingual conversations
    /// </para>
    /// <para>
    /// <b>Example - Voice Switching:</b>
    /// <code>
    /// var options = new AgentRunConfig
    /// {
    ///     Audio = new AudioRunConfig { Voice = "alloy" }
    /// };
    /// await agent.RunAsync(messages, options);
    /// </code>
    /// </para>
    /// <para>
    /// <b>Example - Provider Switching:</b>
    /// <code>
    /// var options = new AgentRunConfig
    /// {
    ///     Audio = new AudioRunConfig
    ///     {
    ///         Tts = new TtsConfig { Provider = "elevenlabs", Voice = "Rachel" }
    ///     }
    /// };
    /// </code>
    /// </para>
    /// <para>
    /// <b>Example - Extension Methods:</b>
    /// <code>
    /// var options = new AgentRunConfig()
    ///     .WithVoice("nova")
    ///     .WithTtsSpeed(1.2f);
    /// </code>
    /// </para>
    /// <para>
    /// Supports both AudioRunConfig (slim runtime API) and AudioConfig (legacy full API).
    /// AudioRunConfig is recommended for runtime customization as it exposes only
    /// commonly-changed settings (voice, provider, language, I/O mode).
    /// </para>
    /// </remarks>
    public object? Audio { get; set; }

    #endregion

    #region History Reduction

    /// <summary>
    /// Force history reduction to trigger for this turn, regardless of automatic thresholds.
    /// Useful for:
    /// - Context switches: Summarize before changing topics
    /// - Before expensive operations: Reduce history before complex tasks
    /// - Memory management: Explicit reduction at strategic points
    /// - Testing: Trigger reduction at specific points in testing
    /// </summary>
    /// <remarks>
    /// When true, history reduction will be performed even if the automatic
    /// thresholds (TargetCount + SummarizationThreshold) are not met.
    /// The reduction will use the configured strategy (Summarizing or MessageCounting).
    /// </remarks>
    public bool TriggerHistoryReduction { get; set; } = false;

    /// <summary>
    /// Skip history reduction for this turn, even if automatic thresholds are met.
    /// Useful for:
    /// - Critical context preservation: Keep full history for important decisions
    /// - Debugging: Disable reduction to inspect full conversation
    /// - Testing: Test behavior without reduction interference
    /// - User preference: Premium users or specific requests need full context
    /// </summary>
    /// <remarks>
    /// When true, history reduction will be skipped for this turn regardless
    /// of whether automatic thresholds would normally trigger it.
    /// Takes precedence over TriggerHistoryReduction if both are set.
    /// </remarks>
    public bool SkipHistoryReduction { get; set; } = false;

    /// <summary>
    /// Override the history reduction behavior for this turn only.
    /// - Continue: Reduction happens transparently, agent continues immediately
    /// - CircuitBreaker: Reduction terminates the turn, user must send next message to continue
    /// Null = use configured default from HistoryReductionConfig.Behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Use Cases:</b>
    /// </para>
    /// <list type="bullet">
    /// <item>Force circuit breaker for critical moments: "Before this important decision, stop and show summary"</item>
    /// <item>Override to Continue for automated flows: "Don't interrupt automated batch processing"</item>
    /// <item>Testing: Switch behaviors per-test to verify both modes work correctly</item>
    /// </list>
    /// </remarks>
    public HistoryReductionBehavior? HistoryReductionBehaviorOverride { get; set; }

    #endregion

    #region Structured Output

    /// <summary>
    /// Configuration for structured output mode.
    /// When set, enables RunStructuredAsync&lt;T&gt;() to return typed responses.
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
    /// await foreach (var evt in agent.RunStructuredAsync&lt;Report&gt;(messages, options: options))
    /// {
    ///     if (evt is StructuredResultEvent&lt;Report&gt; result)
    ///         Console.WriteLine(result.Value);
    /// }
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
    /// await agent.RunAsync(messages, options: options);
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

    #endregion
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
    /// which handles this automatically via RunStructuredAsync&lt;T&gt;().
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

        // Merge: this options take precedence
        var merged = new ChatOptions
        {
            Temperature = thisOptions.Temperature ?? baseOptions.Temperature,
            TopP = thisOptions.TopP ?? baseOptions.TopP,
            TopK = thisOptions.TopK ?? baseOptions.TopK,
            MaxOutputTokens = thisOptions.MaxOutputTokens ?? baseOptions.MaxOutputTokens,
            FrequencyPenalty = thisOptions.FrequencyPenalty ?? baseOptions.FrequencyPenalty,
            PresencePenalty = thisOptions.PresencePenalty ?? baseOptions.PresencePenalty,
            ModelId = thisOptions.ModelId ?? baseOptions.ModelId,
            StopSequences = thisOptions.StopSequences ?? baseOptions.StopSequences,
            Tools = baseOptions.Tools,  // Always from base (tools are agent-level)
            ToolMode = baseOptions.ToolMode,
            ResponseFormat = thisOptions.ResponseFormat ?? baseOptions.ResponseFormat,
            Reasoning = thisOptions.Reasoning ?? baseOptions.Reasoning,
            Seed = baseOptions.Seed
        };

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
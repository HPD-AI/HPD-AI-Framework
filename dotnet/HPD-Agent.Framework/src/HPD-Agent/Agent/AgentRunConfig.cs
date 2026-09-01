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
using HPD.Agent.Permissions;
using HPD.Agent.Providers;
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

/// <summary>Controls how a specialized client role obtains its family acquisition defaults.</summary>
public enum ClientFamilyInheritanceMode
{
    /// <summary>Layer role-specific values over the current run's resolved family.</summary>
    InheritResolved = 0,

    /// <summary>Use only role-owned selection and host fallback.</summary>
    UseOwn = 1,

    /// <summary>Use role-owned selection first and fall back to the resolved parent family.</summary>
    FallbackToParent = 2
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
public sealed record AgentSecurityRunConfig
{
    /// <summary>Gets the protected-operation approval policy.</summary>
    public AgentApprovalPolicy Approval { get; init; } = AgentApprovalPolicy.ReviewProtectedActions;

    /// <summary>Gets the host sandbox policy.</summary>
    public AgentSandboxRunConfig Sandbox { get; init; } = new();

    /// <summary>Gets per-tool permission decisions for this run.</summary>
    public IReadOnlyList<PermissionOverride>? PermissionOverrides { get; init; }
}

/// <summary>Sandbox policy and host capabilities applied to one agent run.</summary>
public sealed record AgentSandboxRunConfig
{
    /// <summary>Gets the host sandbox policy.</summary>
    public AgentSandboxPolicy Mode { get; init; } = AgentSandboxPolicy.Enforced;

    /// <summary>Gets the behavior for capabilities outside enforced sandbox grants.</summary>
    public AgentSandboxEscapePolicy Escape { get; init; } = AgentSandboxEscapePolicy.Ask;

    /// <summary>Gets filesystem, network, and process capabilities granted by the host.</summary>
    public AgentSandboxConfiguration Capabilities { get; init; } = new();
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
/// - Clients.Chat.Override > runtime Clients.Chat selection > agent Clients.Chat defaults
/// - SystemInstructions > Config.SystemInstructions (complete replacement)
/// - SystemInstructions.Append appends to resolved instructions
/// - ContextInstances > Builder-time contexts > Default context
/// </para>
/// </remarks>
public class AgentRunConfig
{
    /// <summary>
    /// Gets or sets capability-targeted subagent policy overrides for this invocation.
    /// These overrides are controller-relative and are never inherited into the child's own run.
    /// </summary>
    public SubAgentRunOverrides SubAgents { get; set; } = new();

    /// <summary>
    /// Security controls for this run.
    /// </summary>
    public AgentSecurityRunConfig Security { get; set; } = new();

    /// <summary>
    /// Provider-created client-family overrides for this run.
    /// </summary>
    public AgentClientsConfig Clients { get; set; } = new();

    [JsonIgnore]
    internal SubAgentClientInheritanceSource? SubAgentClientInheritance { get; set; }

    /// <summary>Gets or sets per-run system-instruction behavior.</summary>
    public SystemInstructionsRunConfig? SystemInstructions { get; set; }

    /// <summary>Gets or sets per-run tool behavior.</summary>
    public AgentToolsRunConfig? Tools { get; set; }

    /// <summary>Gets or sets per-run context values.</summary>
    public AgentContextRunConfig? Context { get; set; }

    /// <summary>Gets or sets per-run background-response behavior.</summary>
    public BackgroundResponsesRunConfig? BackgroundResponses { get; set; }

    /// <summary>Gets or sets per-run streaming behavior.</summary>
    public StreamingRunConfig? Streaming { get; set; }

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

    #region Collapsing

    /// <summary>
    /// Gets or sets per-run overrides for container recovery and model-visible history behavior.
    /// Null means use the agent's configured collapsing defaults.
    /// </summary>
    public CollapsingRunPolicy? Collapsing { get; set; }

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

    /// <summary>Gets or sets package-owned evaluation policy for this run.</summary>
    /// <remarks>
    /// The value is runtime-only because evaluator and judge objects are executable
    /// dependencies. Public run capture calls <see cref="IAgentRunEvaluationConfig.Snapshot"/>
    /// so package-owned mutable configuration is not shared accidentally.
    /// </remarks>
    [JsonIgnore]
    public IAgentRunEvaluationConfig? Evaluations { get; set; }
}

/// <summary>Defines the runtime-only snapshot boundary for package-owned evaluation policy.</summary>
public interface IAgentRunEvaluationConfig
{
    /// <summary>Creates an owned evaluation configuration snapshot for one run.</summary>
    /// <returns>An independent snapshot suitable for the captured invocation.</returns>
    IAgentRunEvaluationConfig Snapshot();
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

    /// <summary>Gets or sets the preferred audio media type for this run.</summary>
    public string? ContentType { get; set; }

    public bool? EnablePlayback { get; set; }
}

/// <summary>
/// Chat-specific run options (JSON-serializable).
/// Subset of Microsoft.Extensions.AI.ChatOptions with only JSON primitives.
/// FFI-friendly - no complex types, no dependencies.
/// </summary>
public sealed class ChatClientConfig : ProviderClientConfig
{
    /// <summary>Gets or sets a borrowed Chat client used only for this configuration scope.</summary>
    [JsonIgnore]
    public ClientOverride<IChatClient>? Override { get; set; }
    /// <summary>
    /// Creates a new instance of ChatClientConfig.
    /// </summary>
    public ChatClientConfig() { }

    /// <summary>
    /// Creates a new instance of ChatClientConfig from Microsoft.Extensions.AI.ChatOptions.
    /// </summary>
    /// <param name="options">The ChatOptions to convert from</param>
    public ChatClientConfig(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Temperature = options.Temperature.HasValue ? (double)options.Temperature.Value : null;
        TopP = options.TopP.HasValue ? (double)options.TopP.Value : null;
        TopK = options.TopK;
        MaxOutputTokens = options.MaxOutputTokens;
        FrequencyPenalty = options.FrequencyPenalty.HasValue ? (double)options.FrequencyPenalty.Value : null;
        PresencePenalty = options.PresencePenalty.HasValue ? (double)options.PresencePenalty.Value : null;
        Seed = options.Seed;
        ModelName = options.ModelId;
        StopSequences = options.StopSequences as IReadOnlyList<string>;
        Reasoning = ReasoningOptions.FromMicrosoftReasoningOptions(options.Reasoning);

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
    /// Stop sequences that signal end of generation.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public IReadOnlyList<string>? StopSequences { get; set; }

    /// <summary>
    /// Provider-specific operation options for the selected Chat provider.
    /// </summary>
    [JsonIgnore]
    public IChatRequestOptions? ProviderOptions { get; set; }

    [JsonIgnore]
    internal ChatResponseFormat? RuntimeResponseFormat { get; set; }

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
    /// Converts to Microsoft.Extensions.AI.ChatOptions for internal use.
    /// Returns null if no overrides are specified.
    /// </summary>
    public ChatOptions? ToMicrosoftChatOptions()
    {
        if (Temperature == null && TopP == null && TopK == null && MaxOutputTokens == null &&
            FrequencyPenalty == null && PresencePenalty == null &&
            Seed == null &&
            string.IsNullOrEmpty(ModelName) && StopSequences == null &&
            Reasoning == null && RuntimeResponseFormat == null &&
            ProviderOptions == null)
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
        if (!string.IsNullOrEmpty(ModelName))
            options.ModelId = ModelName;

        if (StopSequences?.Count > 0)
        {
            options.StopSequences = StopSequences.ToList();
        }

        if (Reasoning != null)
            options.Reasoning = Reasoning.ToMicrosoftReasoningOptions();

        if (RuntimeResponseFormat != null)
            options.ResponseFormat = RuntimeResponseFormat;

        ProviderOptions?.ApplyTo(options);

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

/// <summary>Applies typed provider-specific options to one Chat operation.</summary>
public interface IChatRequestOptions
{
    /// <summary>Applies the provider-owned values to the final MEAI operation options.</summary>
    /// <param name="options">The final operation options being compiled.</param>
    void ApplyTo(ChatOptions options);
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

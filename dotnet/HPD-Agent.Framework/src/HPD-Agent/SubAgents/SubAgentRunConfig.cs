namespace HPD.Agent;

/// <summary>
/// Identifies semantic groups of <see cref="AgentRunConfig"/> values that a child run can inherit.
/// </summary>
[Flags]
public enum SubAgentRunConfigFields
{
    /// <summary>Inherits no parent run-configuration values.</summary>
    None = 0,

    /// <summary>Provider, model, transport, and provider-client selections.</summary>
    Model = 1 << 0,

    /// <summary>Chat generation and reasoning options.</summary>
    Chat = 1 << 1,

    /// <summary>Permission mode and per-tool permission overrides.</summary>
    Permissions = 1 << 2,

    /// <summary>Timeout, caching, streaming, upload, background-response, and audio behavior.</summary>
    Execution = 1 << 3,

    /// <summary>Per-run compaction behavior.</summary>
    Compaction = 1 << 4,

    /// <summary>Runtime context overrides and instances.</summary>
    Context = 1 << 5,

    /// <summary>Replacement and additional system instructions.</summary>
    Instructions = 1 << 6,

    /// <summary>Runtime tools, tool mode, client tools, and client app providers.</summary>
    Tools = 1 << 7,

    /// <summary>Invocation input, attachments, and conversation override.</summary>
    Input = 1 << 8,

    /// <summary>Structured output and custom streaming output behavior.</summary>
    Output = 1 << 9,

    /// <summary>Evaluation settings used by evaluation integrations.</summary>
    Evaluation = 1 << 10,

    /// <summary>
    /// The framework default: inherit the execution environment without replacing the child agent's
    /// instructions, tools, input, output contract, or evaluation behavior.
    /// </summary>
    Default = Model | Chat | Permissions | Execution | Compaction | Context,

    /// <summary>Inherits every run-configuration group.</summary>
    All = Default | Instructions | Tools | Input | Output | Evaluation
}

/// <summary>
/// Controls how a subagent derives its per-run configuration from the invoking parent run.
/// </summary>
/// <remarks>
/// Instances are immutable. Fluent methods return a new selection, so one declaration can be safely
/// reused by synchronous and background invocations.
/// </remarks>
public sealed class SubAgentRunConfig
{
    private readonly Action<AgentRunConfig>? _configure;

    private SubAgentRunConfig(SubAgentRunConfigFields inheritedFields, Action<AgentRunConfig>? configure)
    {
        InheritedFields = inheritedFields;
        _configure = configure;
    }

    /// <summary>Gets the parent run-configuration groups selected for inheritance.</summary>
    public SubAgentRunConfigFields InheritedFields { get; }

    /// <summary>Creates the default parent run-configuration inheritance selection.</summary>
    public static SubAgentRunConfig Inherit()
        => new(SubAgentRunConfigFields.Default, configure: null);

    /// <summary>Creates a selection that inherits exactly the supplied groups.</summary>
    /// <param name="fields">The complete set of groups to inherit.</param>
    public static SubAgentRunConfig InheritOnly(SubAgentRunConfigFields fields)
        => new(ValidateFields(fields), configure: null);

    /// <summary>Creates an isolated child run configuration with no inherited parent values.</summary>
    public static SubAgentRunConfig Isolated()
        => new(SubAgentRunConfigFields.None, configure: null);

    /// <summary>Adds groups to the inheritance selection.</summary>
    public SubAgentRunConfig Include(SubAgentRunConfigFields fields)
        => new(InheritedFields | ValidateFields(fields), _configure);

    /// <summary>Removes groups from the inheritance selection.</summary>
    public SubAgentRunConfig Exclude(SubAgentRunConfigFields fields)
        => new(InheritedFields & ~ValidateFields(fields), _configure);

    /// <summary>
    /// Applies explicit child-only overrides after inherited values have been copied.
    /// </summary>
    /// <param name="configure">A callback that configures the independent child snapshot.</param>
    public SubAgentRunConfig Override(Action<AgentRunConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return new SubAgentRunConfig(
            InheritedFields,
            _configure is null
                ? configure
                : config =>
                {
                    _configure(config);
                    configure(config);
                });
    }

    internal AgentRunConfig Resolve(AgentRunConfig? parent)
    {
        var result = parent is null
            ? new AgentRunConfig()
            : AgentRunConfigInheritance.CreateSnapshot(parent, InheritedFields);
        _configure?.Invoke(result);
        return result;
    }

    private static SubAgentRunConfigFields ValidateFields(SubAgentRunConfigFields fields)
    {
        if ((fields & ~SubAgentRunConfigFields.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(fields), fields, "Unknown subagent run-configuration fields.");
        return fields;
    }
}

internal static class AgentRunConfigInheritance
{
    internal static AgentRunConfig CreateSnapshot(AgentRunConfig source, SubAgentRunConfigFields fields)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new AgentRunConfig();

        if (Has(fields, SubAgentRunConfigFields.Model))
        {
            result.ModelTransport = source.ModelTransport;
            result.Clients = source.Clients;
            result.ProviderKey = source.ProviderKey;
            result.ModelId = source.ModelId;
            result.ApiKey = source.ApiKey;
            result.ProviderEndpoint = source.ProviderEndpoint;
            result.CustomHeaders = source.CustomHeaders is null ? null : new(source.CustomHeaders);
            result.ProviderOptions = source.ProviderOptions?.Clone();
            result.OverrideChatClient = source.OverrideChatClient;
            result.OverrideRealtimeClient = source.OverrideRealtimeClient;
            result.RealtimeTranscriptionOptions = source.RealtimeTranscriptionOptions;
            result.OverrideImageGenerator = source.OverrideImageGenerator;
            result.OverrideEmbeddingGenerator = source.OverrideEmbeddingGenerator;
            result.OverrideHostedFileClient = source.OverrideHostedFileClient;
            result.OverrideVoiceActivityDetectorFactory = source.OverrideVoiceActivityDetectorFactory;
            result.OverrideEndOfTurnDetectorFactory = source.OverrideEndOfTurnDetectorFactory;
        }

        if (Has(fields, SubAgentRunConfigFields.Chat))
            result.Chat = CloneChat(source.Chat);

        if (Has(fields, SubAgentRunConfigFields.Permissions))
        {
            result.Security = source.Security with { };
            result.Sandbox = source.Sandbox with
            {
                Filesystem = source.Sandbox.Filesystem
                    .Select(static grant => grant with { })
                    .ToArray()
            };
            result.PermissionOverrides = source.PermissionOverrides is null ? null : new(source.PermissionOverrides);
        }

        if (Has(fields, SubAgentRunConfigFields.Execution))
        {
            result.RunTimeout = source.RunTimeout;
            result.UseCache = source.UseCache;
            result.SkipTools = source.SkipTools;
            result.CoalesceDeltas = source.CoalesceDeltas;
            result.AllowBackgroundResponses = source.AllowBackgroundResponses;
            result.ContinuationToken = source.ContinuationToken;
            result.BackgroundPollingInterval = source.BackgroundPollingInterval;
            result.BackgroundTimeout = source.BackgroundTimeout;
            result.UploadStrategy = source.UploadStrategy;
            result.Audio = source.Audio;
        }

        if (Has(fields, SubAgentRunConfigFields.Compaction))
            result.Compaction = source.Compaction;

        if (Has(fields, SubAgentRunConfigFields.Context))
        {
            result.ContextOverrides = source.ContextOverrides is null ? null : new(source.ContextOverrides);
            result.ContextInstances = source.ContextInstances is null ? null : new(source.ContextInstances);
        }

        if (Has(fields, SubAgentRunConfigFields.Instructions))
        {
            result.SystemInstructions = source.SystemInstructions;
            result.AdditionalSystemInstructions = source.AdditionalSystemInstructions;
        }

        if (Has(fields, SubAgentRunConfigFields.Tools))
        {
            result.RuntimeMiddleware = source.RuntimeMiddleware?.ToArray();
            result.ClientToolInput = source.ClientToolInput;
            result.ClientAppProviders = source.ClientAppProviders is null ? null : new(source.ClientAppProviders);
            result.AdditionalTools = source.AdditionalTools?.ToArray();
            result.ToolModeOverride = source.ToolModeOverride;
            result.RuntimeTools = source.RuntimeTools is null ? null : new(source.RuntimeTools);
            result.RuntimeToolMode = source.RuntimeToolMode;
        }

        if (Has(fields, SubAgentRunConfigFields.Input))
        {
            result.ConversationIdOverride = source.ConversationIdOverride;
            result.UserMessage = source.UserMessage;
            result.Attachments = source.Attachments?.ToArray();
        }

        if (Has(fields, SubAgentRunConfigFields.Output))
        {
            result.CustomStreamCallback = source.CustomStreamCallback;
            result.StructuredOutput = source.StructuredOutput;
        }

        if (Has(fields, SubAgentRunConfigFields.Evaluation))
        {
            result.DisableEvaluators = source.DisableEvaluators;
            result.IsInternalEvalJudgeCall = source.IsInternalEvalJudgeCall;
            result.AdditionalEvaluators = source.AdditionalEvaluators?.ToArray();
            result.EvaluatorSamplingOverride = source.EvaluatorSamplingOverride;
            result.EvalJudgeConfigOverride = source.EvalJudgeConfigOverride;
        }

        return result;
    }

    private static bool Has(SubAgentRunConfigFields fields, SubAgentRunConfigFields value)
        => (fields & value) == value;

    private static ChatRunConfig? CloneChat(ChatRunConfig? source)
        => source is null
            ? null
            : new ChatRunConfig
            {
                Temperature = source.Temperature,
                TopP = source.TopP,
                TopK = source.TopK,
                MaxOutputTokens = source.MaxOutputTokens,
                FrequencyPenalty = source.FrequencyPenalty,
                PresencePenalty = source.PresencePenalty,
                Seed = source.Seed,
                ModelId = source.ModelId,
                StopSequences = source.StopSequences?.ToArray(),
                AdditionalProperties = source.AdditionalProperties is null ? null : new(source.AdditionalProperties),
                Reasoning = source.Reasoning is null
                    ? null
                    : new ReasoningOptions { Effort = source.Reasoning.Effort, Output = source.Reasoning.Output },
                ResponseFormat = source.ResponseFormat
            };
}

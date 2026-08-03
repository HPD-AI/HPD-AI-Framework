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

    /// <summary>
    /// The framework default: inherit the execution environment without replacing the child agent's
    /// instructions, tools, input, output contract, or evaluation behavior.
    /// </summary>
    Default = Model | Chat | Permissions | Execution | Compaction | Context,

    /// <summary>Inherits every run-configuration group.</summary>
    All = Default | Instructions | Tools | Input | Output
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
            result.Clients.Transport = source.Clients.Transport;
            result.Clients.Chat = CloneChat(source.Clients.Chat);
            result.Clients.Realtime = Clone<RealtimeClientConfig>(source.Clients.Realtime);
            result.Clients.TextToSpeech = Clone<TextToSpeechClientConfig>(source.Clients.TextToSpeech);
            result.Clients.SpeechToText = Clone<SpeechToTextClientConfig>(source.Clients.SpeechToText);
            result.Clients.ImageGeneration = Clone<ImageGenerationClientConfig>(source.Clients.ImageGeneration);
            result.Clients.Embeddings = Clone<EmbeddingsClientConfig>(source.Clients.Embeddings);
            result.Clients.HostedFiles = Clone<HostedFilesClientConfig>(source.Clients.HostedFiles);
            result.Clients.VoiceActivityDetection = Clone<VoiceActivityDetectionClientConfig>(source.Clients.VoiceActivityDetection);
            result.Clients.EndOfTurnDetection = Clone<EndOfTurnDetectionClientConfig>(source.Clients.EndOfTurnDetection);
        }

        if (Has(fields, SubAgentRunConfigFields.Chat))
            result.Clients.Chat = CloneChat(source.Clients.Chat);

        if (Has(fields, SubAgentRunConfigFields.Permissions))
        {
            result.Security = source.Security with
            {
                PermissionOverrides = source.Security.PermissionOverrides is null
                    ? null
                    : new Dictionary<string, bool>(source.Security.PermissionOverrides),
                Sandbox = source.Security.Sandbox with
                {
                    Capabilities = source.Security.Sandbox.Capabilities with
                    {
                        Filesystem = source.Security.Sandbox.Capabilities.Filesystem
                            .Select(static grant => grant with { })
                            .ToArray()
                    }
                }
            };
        }

        if (Has(fields, SubAgentRunConfigFields.Execution))
        {
            result.Streaming = source.Streaming is null ? null : new StreamingRunConfig
            {
                CoalesceDeltas = source.Streaming.CoalesceDeltas,
                Callback = source.Streaming.Callback
            };
            result.BackgroundResponses = source.BackgroundResponses is null ? null : new BackgroundResponsesRunConfig
            {
                Allow = source.BackgroundResponses.Allow,
                ContinuationToken = source.BackgroundResponses.ContinuationToken,
                PollingInterval = source.BackgroundResponses.PollingInterval,
                Timeout = source.BackgroundResponses.Timeout
            };
            result.UploadStrategy = source.UploadStrategy;
            result.Audio = source.Audio;
        }

        if (Has(fields, SubAgentRunConfigFields.Compaction))
            result.Compaction = source.Compaction;

        if (Has(fields, SubAgentRunConfigFields.Context))
        {
            result.Context = source.Context is null ? null : new AgentContextRunConfig
            {
                Properties = source.Context.Properties is null ? null : new Dictionary<string, object>(source.Context.Properties),
                ToolInstances = source.Context.ToolInstances is null ? null : new Dictionary<string, IToolMetadata>(source.Context.ToolInstances)
            };
        }

        if (Has(fields, SubAgentRunConfigFields.Instructions))
        {
            result.SystemInstructions = source.SystemInstructions is null ? null : new SystemInstructionsRunConfig
            {
                Override = source.SystemInstructions.Override,
                Append = source.SystemInstructions.Append
            };
        }

        if (Has(fields, SubAgentRunConfigFields.Tools))
        {
            result.RuntimeMiddleware = source.RuntimeMiddleware?.ToArray();
            result.Tools = source.Tools is null ? null : new AgentToolsRunConfig
            {
                ClientInput = source.Tools.ClientInput,
                ClientAppProviders = source.Tools.ClientAppProviders?.ToArray(),
                Additional = source.Tools.Additional?.ToArray(),
                Mode = source.Tools.Mode
            };
            result.RuntimeTools = source.RuntimeTools is null ? null : new(source.RuntimeTools);
            result.RuntimeToolMode = source.RuntimeToolMode;
        }

        if (Has(fields, SubAgentRunConfigFields.Input))
        {
            result.UserMessage = source.UserMessage;
            result.Attachments = source.Attachments?.ToArray();
        }

        if (Has(fields, SubAgentRunConfigFields.Output))
        {
            result.Streaming = source.Streaming is null ? null : new StreamingRunConfig
            {
                CoalesceDeltas = source.Streaming.CoalesceDeltas,
                Callback = source.Streaming.Callback
            };
            result.StructuredOutput = source.StructuredOutput;
        }

        return result;
    }

    private static TConfig? Clone<TConfig>(TConfig? source)
        where TConfig : ProviderClientConfig
        => source is null ? null : (TConfig)ProviderClientConfigResolver.Clone(source);

    private static bool Has(SubAgentRunConfigFields fields, SubAgentRunConfigFields value)
        => (fields & value) == value;

    private static ChatClientConfig? CloneChat(ChatClientConfig? source)
        => source is null
            ? null
            : new ChatClientConfig
            {
                ProviderKey = source.ProviderKey,
                ModelName = source.ModelName,
                Endpoint = source.Endpoint,
                AuthenticationKey = source.AuthenticationKey,
                ApiKey = source.ApiKey,
                CustomHeaders = source.CustomHeaders is null ? null : new(source.CustomHeaders),
                ProviderConfig = source.ProviderConfig,
                Override = source.Override,
                Temperature = source.Temperature,
                TopP = source.TopP,
                TopK = source.TopK,
                MaxOutputTokens = source.MaxOutputTokens,
                FrequencyPenalty = source.FrequencyPenalty,
                PresencePenalty = source.PresencePenalty,
                Seed = source.Seed,
                StopSequences = source.StopSequences?.ToArray(),
                ProviderOptions = source.ProviderOptions,
                Reasoning = source.Reasoning is null
                    ? null
                    : new ReasoningOptions { Effort = source.Reasoning.Effort, Output = source.Reasoning.Output },
                ResponseFormat = source.ResponseFormat
            };
}

using HPD.Agent.Providers;

namespace HPD.Agent;

/// <summary>
/// Identifies semantic groups of <see cref="AgentRunConfig"/> values that a child run can inherit.
/// </summary>
[Flags]
public enum SubAgentRunConfigFields
{
    /// <summary>Inherits no parent run-configuration values.</summary>
    None = 0,

    /// <summary>Permission mode and per-tool permission overrides.</summary>
    Permissions = 1 << 0,

    /// <summary>Timeout, caching, streaming, upload, background-response, and audio behavior.</summary>
    Execution = 1 << 1,

    /// <summary>Per-run compaction behavior.</summary>
    Compaction = 1 << 2,

    /// <summary>Runtime context overrides and instances.</summary>
    Context = 1 << 3,

    /// <summary>Replacement and additional system instructions.</summary>
    Instructions = 1 << 4,

    /// <summary>Runtime tools, tool mode, client tools, and client app providers.</summary>
    Tools = 1 << 5,

    /// <summary>Structured output and custom streaming output behavior.</summary>
    Output = 1 << 6,

    /// <summary>
    /// The framework default: inherit the execution environment without replacing the child agent's
    /// instructions, tools, input, output contract, or evaluation behavior.
    /// </summary>
    Default = Permissions | Execution | Compaction | Context,

    /// <summary>Inherits every run-configuration group.</summary>
    All = Default | Instructions | Tools | Output
}

/// <summary>Controls how each child client family relates to the invoking run.</summary>
public sealed record AgentClientInheritance
{
    /// <summary>Gets the Chat-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode Chat { get; init; } = ClientFamilyInheritanceMode.InheritResolved;

    /// <summary>Gets the Realtime-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode Realtime { get; init; } = ClientFamilyInheritanceMode.InheritResolved;

    /// <summary>Gets the image-generation-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode ImageGeneration { get; init; } = ClientFamilyInheritanceMode.UseOwn;

    /// <summary>Gets the embeddings-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode Embeddings { get; init; } = ClientFamilyInheritanceMode.UseOwn;

    /// <summary>Gets the text-to-speech-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode TextToSpeech { get; init; } = ClientFamilyInheritanceMode.InheritResolved;

    /// <summary>Gets the speech-to-text-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode SpeechToText { get; init; } = ClientFamilyInheritanceMode.InheritResolved;

    /// <summary>Gets the hosted-files-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode HostedFiles { get; init; } = ClientFamilyInheritanceMode.FallbackToParent;
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

    private SubAgentRunConfig(
        SubAgentRunConfigFields inheritedFields,
        AgentClientInheritance clients,
        Action<AgentRunConfig>? configure)
    {
        InheritedFields = inheritedFields;
        Clients = clients;
        _configure = configure;
    }

    /// <summary>Gets the parent run-configuration groups selected for inheritance.</summary>
    public SubAgentRunConfigFields InheritedFields { get; }

    /// <summary>Gets the explicit inheritance policy for every client family.</summary>
    public AgentClientInheritance Clients { get; }

    /// <summary>Creates the default parent run-configuration inheritance selection.</summary>
    public static SubAgentRunConfig Inherit()
        => new(SubAgentRunConfigFields.Default, new AgentClientInheritance(), configure: null);

    /// <summary>Creates a selection that inherits exactly the supplied groups.</summary>
    /// <param name="fields">The complete set of groups to inherit.</param>
    public static SubAgentRunConfig InheritOnly(SubAgentRunConfigFields fields)
        => new(ValidateFields(fields), new AgentClientInheritance(), configure: null);

    /// <summary>Creates an isolated child run configuration with no inherited parent values.</summary>
    public static SubAgentRunConfig Isolated()
        => new(SubAgentRunConfigFields.None, CreateUseOwnClients(), configure: null);

    /// <summary>Returns a new selection with the supplied per-family client inheritance policy.</summary>
    /// <param name="clients">The complete client-family inheritance policy.</param>
    public SubAgentRunConfig WithClients(AgentClientInheritance clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        return new SubAgentRunConfig(InheritedFields, clients, _configure);
    }

    /// <summary>Adds groups to the inheritance selection.</summary>
    public SubAgentRunConfig Include(SubAgentRunConfigFields fields)
        => new(InheritedFields | ValidateFields(fields), Clients, _configure);

    /// <summary>Removes groups from the inheritance selection.</summary>
    public SubAgentRunConfig Exclude(SubAgentRunConfigFields fields)
        => new(InheritedFields & ~ValidateFields(fields), Clients, _configure);

    /// <summary>
    /// Applies explicit child-only overrides after inherited values have been copied.
    /// </summary>
    /// <param name="configure">A callback that configures the independent child snapshot.</param>
    public SubAgentRunConfig Override(Action<AgentRunConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return new SubAgentRunConfig(
            InheritedFields,
            Clients,
            _configure is null
                ? configure
                : config =>
                {
                    _configure(config);
                    configure(config);
                });
    }

    private static AgentClientInheritance CreateUseOwnClients() => new()
    {
        Chat = ClientFamilyInheritanceMode.UseOwn,
        Realtime = ClientFamilyInheritanceMode.UseOwn,
        ImageGeneration = ClientFamilyInheritanceMode.UseOwn,
        Embeddings = ClientFamilyInheritanceMode.UseOwn,
        TextToSpeech = ClientFamilyInheritanceMode.UseOwn,
        SpeechToText = ClientFamilyInheritanceMode.UseOwn,
        HostedFiles = ClientFamilyInheritanceMode.UseOwn
    };

    internal AgentRunConfig Resolve(
        AgentRunConfig? parent,
        AgentClientSet? parentClients = null,
        AgentConfig? childDefaults = null,
        ProviderComposition? composition = null)
    {
        var result = parent is null
            ? new AgentRunConfig()
            : AgentRunConfigInheritance.CreateSnapshot(parent, InheritedFields, composition);
        _configure?.Invoke(result);
        ApplyClientInheritance(result, parentClients, childDefaults, Clients);
        return result;
    }

    internal static void ApplyClientInheritance(
        AgentRunConfig result,
        AgentClientSet? parentClients,
        AgentConfig? childDefaults,
        AgentClientInheritance clients)
    {
        if (parentClients is null)
            return;

        InheritFamily(ProviderClientFamily.Realtime, clients.Realtime);
        InheritFamily(ProviderClientFamily.ImageGeneration, clients.ImageGeneration);
        InheritFamily(ProviderClientFamily.Embeddings, clients.Embeddings);
        InheritFamily(ProviderClientFamily.TextToSpeech, clients.TextToSpeech);
        InheritFamily(ProviderClientFamily.SpeechToText, clients.SpeechToText);
        InheritFamily(ProviderClientFamily.HostedFiles, clients.HostedFiles);

        void InheritFamily(ProviderClientFamily family, ClientFamilyInheritanceMode mode)
        {
            if (mode == ClientFamilyInheritanceMode.UseOwn)
                return;

            var own = result.Clients.GetFamilyConfig(family);
            if (mode == ClientFamilyInheritanceMode.FallbackToParent &&
                (own is not null || childDefaults?.ResolveClientConfig(family) is not null))
                return;

            var parent = parentClients.GetResolvedConfig(family);
            var parentClient = GetClient(parentClients, family);
            if (parent is null || parentClient is null)
                return;

            var inherited = ProviderClientConfigSnapshot.Clone(parent);
            if (own is not null)
                inherited = EffectiveProviderClientConfigResolver.OverlayResolvedInheritance(parent, own);
            if (own?.Provider is null)
                SetOverride(inherited, family, parentClient);
            result.Clients.SetFamilyConfig(family, inherited);
        }
    }

    private static object? GetClient(AgentClientSet clients, ProviderClientFamily family) => family switch
    {
        ProviderClientFamily.Realtime => clients.Realtime,
        ProviderClientFamily.ImageGeneration => clients.ImageGenerator,
        ProviderClientFamily.Embeddings => clients.EmbeddingGenerator,
        ProviderClientFamily.TextToSpeech => clients.TextToSpeech,
        ProviderClientFamily.SpeechToText => clients.SpeechToText,
        ProviderClientFamily.HostedFiles => clients.HostedFiles,
        _ => null
    };

    private static void SetOverride(
        ProviderClientConfig config,
        ProviderClientFamily family,
        object client)
    {
        switch (family)
        {
            case ProviderClientFamily.Realtime:
                ((RealtimeClientConfig)config).Override = ClientOverride<Microsoft.Extensions.AI.IRealtimeClient>.Borrow((Microsoft.Extensions.AI.IRealtimeClient)client);
                break;
            case ProviderClientFamily.ImageGeneration:
                ((ImageGenerationClientConfig)config).Override = ClientOverride<Microsoft.Extensions.AI.IImageGenerator>.Borrow((Microsoft.Extensions.AI.IImageGenerator)client);
                break;
            case ProviderClientFamily.Embeddings:
                ((EmbeddingsClientConfig)config).Override = ClientOverride<Microsoft.Extensions.AI.IEmbeddingGenerator>.Borrow((Microsoft.Extensions.AI.IEmbeddingGenerator)client);
                break;
            case ProviderClientFamily.TextToSpeech:
                ((TextToSpeechClientConfig)config).Override = ClientOverride<Microsoft.Extensions.AI.ITextToSpeechClient>.Borrow((Microsoft.Extensions.AI.ITextToSpeechClient)client);
                break;
            case ProviderClientFamily.SpeechToText:
                ((SpeechToTextClientConfig)config).Override = ClientOverride<Microsoft.Extensions.AI.ISpeechToTextClient>.Borrow((Microsoft.Extensions.AI.ISpeechToTextClient)client);
                break;
            case ProviderClientFamily.HostedFiles:
                ((HostedFilesClientConfig)config).Override = ClientOverride<Microsoft.Extensions.AI.IHostedFileClient>.Borrow((Microsoft.Extensions.AI.IHostedFileClient)client);
                break;
        }
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
    internal static AgentRunConfig CreateSnapshot(
        AgentRunConfig source,
        SubAgentRunConfigFields fields,
        ProviderComposition? composition = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new AgentRunConfig();

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
            result.Audio = AgentRunConfigSnapshot.CloneAudio(source.Audio);
        }

        if (Has(fields, SubAgentRunConfigFields.Compaction))
            result.Compaction = AgentRunConfigSnapshot.CloneCompaction(source.Compaction, composition);

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

        if (Has(fields, SubAgentRunConfigFields.Output))
        {
            result.Streaming = source.Streaming is null ? null : new StreamingRunConfig
            {
                CoalesceDeltas = source.Streaming.CoalesceDeltas,
                Callback = source.Streaming.Callback
            };
            result.StructuredOutput = AgentRunConfigSnapshot.CloneStructuredOutput(source.StructuredOutput);
        }

        return result;
    }

    private static bool Has(SubAgentRunConfigFields fields, SubAgentRunConfigFields value)
        => (fields & value) == value;
}

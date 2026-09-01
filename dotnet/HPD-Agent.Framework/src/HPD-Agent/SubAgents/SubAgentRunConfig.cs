using System.Security.Cryptography;
using System.Text;
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

    /// <summary>Per-run container recovery and model-visible history behavior.</summary>
    Collapsing = 1 << 7,

    /// <summary>
    /// The framework default: inherit the execution environment without replacing the child agent's
    /// instructions, tools, input, output contract, or evaluation behavior.
    /// </summary>
    Default = Permissions | Execution | Compaction | Context | Collapsing,

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

    /// <summary>Gets the voice-activity-detection-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode VoiceActivityDetection { get; init; } = ClientFamilyInheritanceMode.UseOwn;

    /// <summary>Gets the end-of-turn-detection-family inheritance policy.</summary>
    public ClientFamilyInheritanceMode EndOfTurnDetection { get; init; } = ClientFamilyInheritanceMode.UseOwn;
}

/// <summary>Declares which parent-run policy changes a subagent author permits.</summary>
public sealed record SubAgentRunPolicyOverrideAllowance
{
    /// <summary>Gets fields that a parent run may add to the declaration's inherited fields.</summary>
    public SubAgentRunConfigFields MayEnableInheritedFields { get; init; }

    /// <summary>Gets fields that a parent run may remove from the declaration's inherited fields.</summary>
    public SubAgentRunConfigFields MayDisableInheritedFields { get; init; }

    /// <summary>Gets the client families whose inheritance mode a parent run may replace.</summary>
    public AgentClientInheritanceOverrideAllowance Clients { get; init; } = new();
}

/// <summary>Declares per-family permission for a parent-run inheritance-mode override.</summary>
public sealed record AgentClientInheritanceOverrideAllowance
{
    /// <summary>Gets whether Chat may be overridden.</summary>
    public bool Chat { get; init; }
    /// <summary>Gets whether text-to-speech may be overridden.</summary>
    public bool TextToSpeech { get; init; }
    /// <summary>Gets whether speech-to-text may be overridden.</summary>
    public bool SpeechToText { get; init; }
    /// <summary>Gets whether Realtime may be overridden.</summary>
    public bool Realtime { get; init; }
    /// <summary>Gets whether image generation may be overridden.</summary>
    public bool ImageGeneration { get; init; }
    /// <summary>Gets whether embeddings may be overridden.</summary>
    public bool Embeddings { get; init; }
    /// <summary>Gets whether hosted files may be overridden.</summary>
    public bool HostedFiles { get; init; }
    /// <summary>Gets whether voice-activity detection may be overridden.</summary>
    public bool VoiceActivityDetection { get; init; }
    /// <summary>Gets whether end-of-turn detection may be overridden.</summary>
    public bool EndOfTurnDetection { get; init; }
}

/// <summary>Contains optional parent-run replacements for client-family inheritance modes.</summary>
public sealed record AgentClientInheritancePatch
{
    /// <summary>Gets the optional Chat replacement.</summary>
    public ClientFamilyInheritanceMode? Chat { get; init; }
    /// <summary>Gets the optional text-to-speech replacement.</summary>
    public ClientFamilyInheritanceMode? TextToSpeech { get; init; }
    /// <summary>Gets the optional speech-to-text replacement.</summary>
    public ClientFamilyInheritanceMode? SpeechToText { get; init; }
    /// <summary>Gets the optional Realtime replacement.</summary>
    public ClientFamilyInheritanceMode? Realtime { get; init; }
    /// <summary>Gets the optional image-generation replacement.</summary>
    public ClientFamilyInheritanceMode? ImageGeneration { get; init; }
    /// <summary>Gets the optional embeddings replacement.</summary>
    public ClientFamilyInheritanceMode? Embeddings { get; init; }
    /// <summary>Gets the optional hosted-files replacement.</summary>
    public ClientFamilyInheritanceMode? HostedFiles { get; init; }
    /// <summary>Gets the optional voice-activity-detection replacement.</summary>
    public ClientFamilyInheritanceMode? VoiceActivityDetection { get; init; }
    /// <summary>Gets the optional end-of-turn-detection replacement.</summary>
    public ClientFamilyInheritanceMode? EndOfTurnDetection { get; init; }
}

/// <summary>Overrides one declared subagent policy for a single parent invocation.</summary>
public sealed record SubAgentRunPolicyOverride
{
    /// <summary>Gets the stable generated capability to override.</summary>
    public required CapabilityId CapabilityId { get; init; }

    /// <summary>Gets a complete replacement inherited-field set, or <see langword="null"/>.</summary>
    public SubAgentRunConfigFields? InheritedFields { get; init; }

    /// <summary>Gets per-family inheritance-mode replacements.</summary>
    public AgentClientInheritancePatch? Clients { get; init; }
}

/// <summary>Contains capability-targeted subagent policy overrides for one parent invocation.</summary>
public sealed record SubAgentRunOverrides
{
    /// <summary>Gets the canonical capability override list.</summary>
    public IReadOnlyList<SubAgentRunPolicyOverride> Capabilities { get; init; } = [];
}

/// <summary>Immutable versioned policy required to reconstruct a durable child execution.</summary>
public sealed record SubAgentExecutionPolicy
{
    /// <summary>The only policy contract version understood by this runtime.</summary>
    public const int CurrentContractVersion = 1;

    /// <summary>Gets the durable policy contract version.</summary>
    public required int ContractVersion { get; init; }

    /// <summary>Gets the parent run-configuration groups eligible for inheritance.</summary>
    public required SubAgentRunConfigFields InheritedFields { get; init; }

    /// <summary>Gets the complete nine-family client inheritance policy.</summary>
    public required AgentClientInheritance Clients { get; init; }

    /// <summary>Gets the canonical SHA-256 policy fingerprint.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Reconstructs the child run configuration represented by this durable policy.</summary>
    /// <param name="controllingRun">The current authorized controller's run configuration.</param>
    /// <param name="controllingClients">The current controller's execution-scoped client set.</param>
    /// <param name="childDefaults">The durable child agent configuration.</param>
    /// <param name="composition">The generated provider composition used to snapshot provider payloads.</param>
    /// <returns>An independent child run configuration with lazy client-family inheritance installed.</returns>
    public AgentRunConfig CreateChildRunConfig(
        AgentRunConfig? controllingRun = null,
        AgentClientSet? controllingClients = null,
        AgentConfig? childDefaults = null,
        ProviderComposition? composition = null)
    {
        Validate();
        return SubAgentRunConfig.Resolve(
            this, controllingRun, controllingClients, childDefaults, composition);
    }

    internal static SubAgentExecutionPolicy Create(
        SubAgentRunConfigFields inheritedFields,
        AgentClientInheritance clients)
    {
        SubAgentRunConfig.ValidateFields(inheritedFields);
        ValidateClients(clients);
        return new SubAgentExecutionPolicy
        {
            ContractVersion = CurrentContractVersion,
            InheritedFields = inheritedFields,
            Clients = clients with { },
            Fingerprint = ComputeFingerprint(CurrentContractVersion, inheritedFields, clients)
        };
    }

    internal void Validate()
    {
        if (ContractVersion != CurrentContractVersion)
            throw new InvalidOperationException("subagent_execution_policy_invalid");
        SubAgentRunConfig.ValidateFields(InheritedFields);
        ValidateClients(Clients);
        if (!string.Equals(
                Fingerprint,
                ComputeFingerprint(ContractVersion, InheritedFields, Clients),
                StringComparison.Ordinal))
            throw new InvalidOperationException("subagent_execution_policy_mismatch");
    }

    private static string ComputeFingerprint(
        int version,
        SubAgentRunConfigFields fields,
        AgentClientInheritance clients)
    {
        var canonical = string.Join("|", new[]
        {
            "hpd.subagent.execution-policy",
            version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)fields).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.Chat).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.TextToSpeech).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.SpeechToText).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.Realtime).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.ImageGeneration).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.Embeddings).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.HostedFiles).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.VoiceActivityDetection).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)clients.EndOfTurnDetection).ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void ValidateClients(AgentClientInheritance clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        foreach (var value in new[]
        {
            clients.Chat, clients.TextToSpeech, clients.SpeechToText, clients.Realtime,
            clients.ImageGeneration, clients.Embeddings, clients.HostedFiles,
            clients.VoiceActivityDetection, clients.EndOfTurnDetection
        })
            if (!Enum.IsDefined(value))
                throw new InvalidOperationException("subagent_execution_policy_invalid");
    }
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
    private SubAgentRunConfig(
        SubAgentRunConfigFields inheritedFields,
        AgentClientInheritance clients,
        SubAgentRunPolicyOverrideAllowance overrideAllowance)
    {
        InheritedFields = inheritedFields;
        Clients = clients;
        OverrideAllowance = overrideAllowance;
    }

    /// <summary>Gets the parent run-configuration groups selected for inheritance.</summary>
    public SubAgentRunConfigFields InheritedFields { get; }

    /// <summary>Gets the explicit inheritance policy for every client family.</summary>
    public AgentClientInheritance Clients { get; }

    /// <summary>Gets the parent-run policy changes admitted by this declaration.</summary>
    public SubAgentRunPolicyOverrideAllowance OverrideAllowance { get; }

    /// <summary>Creates the default parent run-configuration inheritance selection.</summary>
    public static SubAgentRunConfig Inherit()
        => new(SubAgentRunConfigFields.Default, new AgentClientInheritance(), new());

    /// <summary>Creates a selection that inherits exactly the supplied groups.</summary>
    /// <param name="fields">The complete set of groups to inherit.</param>
    public static SubAgentRunConfig InheritOnly(SubAgentRunConfigFields fields)
        => new(ValidateFields(fields), new AgentClientInheritance(), new());

    /// <summary>Creates an isolated child run configuration with no inherited parent values.</summary>
    public static SubAgentRunConfig Isolated()
        => new(SubAgentRunConfigFields.None, CreateUseOwnClients(), new());

    /// <summary>Returns a new selection with the supplied per-family client inheritance policy.</summary>
    /// <param name="clients">The complete client-family inheritance policy.</param>
    public SubAgentRunConfig WithClients(AgentClientInheritance clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        return new SubAgentRunConfig(InheritedFields, clients with { }, OverrideAllowance);
    }

    /// <summary>Adds groups to the inheritance selection.</summary>
    public SubAgentRunConfig Include(SubAgentRunConfigFields fields)
        => new(InheritedFields | ValidateFields(fields), Clients, OverrideAllowance);

    /// <summary>Removes groups from the inheritance selection.</summary>
    public SubAgentRunConfig Exclude(SubAgentRunConfigFields fields)
        => new(InheritedFields & ~ValidateFields(fields), Clients, OverrideAllowance);

    /// <summary>Returns a declaration that admits the supplied parent-run policy changes.</summary>
    public SubAgentRunConfig AllowParentRunOverrides(SubAgentRunPolicyOverrideAllowance allowance)
    {
        ArgumentNullException.ThrowIfNull(allowance);
        ValidateFields(allowance.MayEnableInheritedFields);
        ValidateFields(allowance.MayDisableInheritedFields);
        return new SubAgentRunConfig(InheritedFields, Clients, allowance with { Clients = allowance.Clients with { } });
    }

    internal SubAgentExecutionPolicy Compile(SubAgentRunPolicyOverride? runOverride = null)
    {
        if (runOverride is null)
            return SubAgentExecutionPolicy.Create(InheritedFields, Clients);

        var fields = runOverride.InheritedFields ?? InheritedFields;
        ValidateFields(fields);
        var enabled = fields & ~InheritedFields;
        var disabled = InheritedFields & ~fields;
        if ((enabled & ~OverrideAllowance.MayEnableInheritedFields) != 0 ||
            (disabled & ~OverrideAllowance.MayDisableInheritedFields) != 0)
            throw new InvalidOperationException("subagent_override_inherited_field_not_permitted");

        var clients = ApplyClientPatch(Clients, runOverride.Clients, OverrideAllowance.Clients);
        return SubAgentExecutionPolicy.Create(fields, clients);
    }

    /// <summary>Compiles this declaration into the immutable policy persisted with a durable child.</summary>
    /// <returns>A validated, fingerprinted execution policy.</returns>
    public SubAgentExecutionPolicy CompilePolicy() => Compile();

    private static AgentClientInheritance CreateUseOwnClients() => new()
    {
        Chat = ClientFamilyInheritanceMode.UseOwn,
        Realtime = ClientFamilyInheritanceMode.UseOwn,
        ImageGeneration = ClientFamilyInheritanceMode.UseOwn,
        Embeddings = ClientFamilyInheritanceMode.UseOwn,
        TextToSpeech = ClientFamilyInheritanceMode.UseOwn,
        SpeechToText = ClientFamilyInheritanceMode.UseOwn,
        HostedFiles = ClientFamilyInheritanceMode.UseOwn,
        VoiceActivityDetection = ClientFamilyInheritanceMode.UseOwn,
        EndOfTurnDetection = ClientFamilyInheritanceMode.UseOwn
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
        ApplyClientInheritance(result, parentClients, childDefaults, Clients);
        return result;
    }

    internal static AgentRunConfig Resolve(
        SubAgentExecutionPolicy policy,
        AgentRunConfig? parent,
        AgentClientSet? parentClients = null,
        AgentConfig? childDefaults = null,
        ProviderComposition? composition = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        var result = parent is null
            ? new AgentRunConfig()
            : AgentRunConfigInheritance.CreateSnapshot(parent, policy.InheritedFields, composition);
        ApplyClientInheritance(result, parentClients, childDefaults, policy.Clients);
        return result;
    }

    private static AgentClientInheritance ApplyClientPatch(
        AgentClientInheritance source,
        AgentClientInheritancePatch? patch,
        AgentClientInheritanceOverrideAllowance allowance)
    {
        if (patch is null) return source with { };
        ClientFamilyInheritanceMode Pick(
            ClientFamilyInheritanceMode current,
            ClientFamilyInheritanceMode? requested,
            bool permitted)
        {
            if (requested is null) return current;
            if (!permitted) throw new InvalidOperationException("subagent_client_inheritance_not_permitted");
            if (!Enum.IsDefined(requested.Value))
                throw new InvalidOperationException("subagent_execution_policy_invalid");
            return requested.Value;
        }
        return new AgentClientInheritance
        {
            Chat = Pick(source.Chat, patch.Chat, allowance.Chat),
            TextToSpeech = Pick(source.TextToSpeech, patch.TextToSpeech, allowance.TextToSpeech),
            SpeechToText = Pick(source.SpeechToText, patch.SpeechToText, allowance.SpeechToText),
            Realtime = Pick(source.Realtime, patch.Realtime, allowance.Realtime),
            ImageGeneration = Pick(source.ImageGeneration, patch.ImageGeneration, allowance.ImageGeneration),
            Embeddings = Pick(source.Embeddings, patch.Embeddings, allowance.Embeddings),
            HostedFiles = Pick(source.HostedFiles, patch.HostedFiles, allowance.HostedFiles),
            VoiceActivityDetection = Pick(source.VoiceActivityDetection, patch.VoiceActivityDetection, allowance.VoiceActivityDetection),
            EndOfTurnDetection = Pick(source.EndOfTurnDetection, patch.EndOfTurnDetection, allowance.EndOfTurnDetection)
        };
    }

    internal static void ApplyClientInheritance(
        AgentRunConfig result,
        AgentClientSet? parentClients,
        AgentConfig? childDefaults,
        AgentClientInheritance clients)
    {
        result.SubAgentClientInheritance = new SubAgentClientInheritanceSource(parentClients, clients);
    }

    internal static SubAgentRunConfigFields ValidateFields(SubAgentRunConfigFields fields)
    {
        if ((fields & ~SubAgentRunConfigFields.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(fields), fields, "Unknown subagent run-configuration fields.");
        return fields;
    }
}

internal sealed record SubAgentClientInheritanceSource(
    AgentClientSet? ParentClients,
    AgentClientInheritance Modes)
{
    internal ClientFamilyInheritanceMode GetMode(ProviderClientFamily family) => family switch
    {
        ProviderClientFamily.Chat => Modes.Chat,
        ProviderClientFamily.Realtime => Modes.Realtime,
        ProviderClientFamily.ImageGeneration => Modes.ImageGeneration,
        ProviderClientFamily.Embeddings => Modes.Embeddings,
        ProviderClientFamily.TextToSpeech => Modes.TextToSpeech,
        ProviderClientFamily.SpeechToText => Modes.SpeechToText,
        ProviderClientFamily.HostedFiles => Modes.HostedFiles,
        ProviderClientFamily.VoiceActivityDetection => Modes.VoiceActivityDetection,
        ProviderClientFamily.EndOfTurnDetection => Modes.EndOfTurnDetection,
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };
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
                    : source.Security.PermissionOverrides.Select(static value => value with
                    {
                        Selector = value.Selector with { }
                    }).ToArray(),
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

        if (Has(fields, SubAgentRunConfigFields.Collapsing))
            result.Collapsing = AgentRunConfigSnapshot.CloneCollapsing(source.Collapsing);

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

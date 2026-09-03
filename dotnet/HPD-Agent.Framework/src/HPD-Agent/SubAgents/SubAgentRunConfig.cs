using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;

namespace HPD.Agent;

/// <summary>
/// Configures every direct subagent invocation initiated while processing one agent input.
/// </summary>
/// <remarks>
/// The inherited <see cref="AgentRunConfig"/> properties configure each direct child. Only an
/// explicit portable Chat selection can propagate to deeper descendants.
/// </remarks>
public sealed class SubAgentRunConfig : AgentRunConfig
{
    /// <summary>Gets or sets how far an explicit Chat selection propagates through descendants.</summary>
    public SubAgentClientPropagation ClientPropagation { get; set; } =
        SubAgentClientPropagation.DirectChildren;
}

/// <summary>Controls descendant propagation of an explicit subagent Chat selection.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DirectSubAgentClientPropagation), "direct")]
[JsonDerivedType(typeof(BoundedSubAgentClientPropagation), "bounded")]
[JsonDerivedType(typeof(UnboundedSubAgentClientPropagation), "unbounded")]
public abstract record SubAgentClientPropagation
{
    /// <summary>Applies the selection only to direct children.</summary>
    public static SubAgentClientPropagation DirectChildren { get; } = new DirectSubAgentClientPropagation();

    /// <summary>Applies the selection through the supplied positive number of descendant levels.</summary>
    public static SubAgentClientPropagation ThroughDepth(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        return depth == 1 ? DirectChildren : new BoundedSubAgentClientPropagation(depth);
    }

    /// <summary>Applies the selection to the complete descendant tree.</summary>
    public static SubAgentClientPropagation EntireTree { get; } = new UnboundedSubAgentClientPropagation();
}

/// <summary>Applies an explicit selection to direct children only.</summary>
public sealed record DirectSubAgentClientPropagation : SubAgentClientPropagation;

/// <summary>Applies an explicit selection through a positive number of descendant levels.</summary>
/// <param name="Depth">Number of levels including direct children.</param>
public sealed record BoundedSubAgentClientPropagation(int Depth) : SubAgentClientPropagation;

/// <summary>Applies an explicit selection to every descendant level.</summary>
public sealed record UnboundedSubAgentClientPropagation : SubAgentClientPropagation;

/// <summary>Durable remaining propagation state stored with an admitted child.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NoSubAgentClientPropagation), "none")]
[JsonDerivedType(typeof(RemainingSubAgentClientPropagation), "bounded")]
[JsonDerivedType(typeof(UnboundedRemainingSubAgentClientPropagation), "unbounded")]
public abstract record SubAgentClientPropagationState;

/// <summary>No client-selection propagation remains.</summary>
public sealed record NoSubAgentClientPropagation : SubAgentClientPropagationState;

/// <summary>A bounded number of further descendant levels remains.</summary>
/// <param name="RemainingDepth">Positive number of remaining levels.</param>
public sealed record RemainingSubAgentClientPropagation(int RemainingDepth) : SubAgentClientPropagationState;

/// <summary>Client selection continues through the entire descendant tree.</summary>
public sealed record UnboundedRemainingSubAgentClientPropagation : SubAgentClientPropagationState;

/// <summary>Identifies the source that won durable subagent Chat selection.</summary>
public enum SubAgentClientSelectionSource
{
    /// <summary>The input-scoped child run selected the client.</summary>
    InputSubAgentRun,
    /// <summary>The child agent definition selected the client.</summary>
    ChildAgentConfig,
    /// <summary>The invoking controller's resolved client selected the client.</summary>
    ControllerResolved
}

/// <summary>Immutable versioned policy required to reconstruct an admitted durable child.</summary>
public sealed record SubAgentExecutionPolicy
{
    /// <summary>The only policy contract understood by this greenfield runtime.</summary>
    public const int CurrentContractVersion = 2;

    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }

    /// <summary>Gets the portable initial child run configuration.</summary>
    public AgentRunConfig? InitialRunConfig { get; init; }

    /// <summary>Gets the complete portable client-family selections locked at admission.</summary>
    public required AgentClientsConfig LockedClients { get; init; }

    /// <summary>Gets the winning source for every available locked client family.</summary>
    public required IReadOnlyDictionary<ProviderClientFamily, SubAgentClientSelectionSource> ClientSources { get; init; }

    /// <summary>Gets the controller-bounded security authority locked at admission.</summary>
    public required AgentSecurityRunConfig Authority { get; init; }

    /// <summary>Gets the remaining descendant Chat propagation state.</summary>
    public required SubAgentClientPropagationState Propagation { get; init; }

    /// <summary>Gets the canonical policy fingerprint.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Creates a validated durable policy from an admitted child run and locked chat selection.</summary>
    public static SubAgentExecutionPolicy Create(
        AgentRunConfig? initialRunConfig,
        AgentClientsConfig lockedClients,
        IReadOnlyDictionary<ProviderClientFamily, SubAgentClientSelectionSource> clientSources,
        AgentSecurityRunConfig authority,
        SubAgentClientPropagationState propagation)
    {
        ArgumentNullException.ThrowIfNull(lockedClients);
        ArgumentNullException.ThrowIfNull(clientSources);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(propagation);
        ValidatePortableInitialRun(initialRunConfig);
        var lockedSnapshot = CloneClients(lockedClients);
        var sourceSnapshot = new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource>(clientSources);
        var authoritySnapshot = authority with
        {
            PermissionOverrides = authority.PermissionOverrides?.Select(static value => value with
            {
                Selector = value.Selector with { }
            }).ToArray(),
            Sandbox = authority.Sandbox with
            {
                Capabilities = authority.Sandbox.Capabilities with
                {
                    Filesystem = authority.Sandbox.Capabilities.Filesystem.Select(static value => value with { }).ToArray()
                }
            }
        };
        var fingerprint = ComputeFingerprint(initialRunConfig, lockedSnapshot, sourceSnapshot, authoritySnapshot, propagation);
        return new SubAgentExecutionPolicy
        {
            ContractVersion = CurrentContractVersion,
            InitialRunConfig = initialRunConfig,
            LockedClients = lockedSnapshot,
            ClientSources = sourceSnapshot,
            Authority = authoritySnapshot,
            Propagation = propagation,
            Fingerprint = fingerprint
        };
    }

    internal void Validate()
    {
        if (ContractVersion != CurrentContractVersion || LockedClients.Chat is null)
            throw new InvalidOperationException("subagent_execution_policy_invalid");
        foreach (var family in Enum.GetValues<ProviderClientFamily>())
        {
            var config = LockedClients.GetFamilyConfig(family);
            if (config is null)
            {
                if (ClientSources.ContainsKey(family))
                    throw new InvalidOperationException("subagent_execution_policy_invalid");
                continue;
            }
            if (!ClientSources.ContainsKey(family) || HasRuntimeOverride(config) || HasProviderPayload(config))
                throw new InvalidOperationException("subagent_execution_policy_invalid");
        }
        ValidatePropagation(Propagation);
        ValidatePortableInitialRun(InitialRunConfig);
        if (!string.Equals(Fingerprint, ComputeFingerprint(InitialRunConfig, LockedClients, ClientSources, Authority, Propagation), StringComparison.Ordinal))
            throw new InvalidOperationException("subagent_execution_policy_mismatch");
    }

    private static void ValidatePropagation(SubAgentClientPropagationState value)
    {
        if (value is RemainingSubAgentClientPropagation bounded && bounded.RemainingDepth < 1)
            throw new InvalidOperationException("subagent_execution_policy_invalid");
        if (value is not (NoSubAgentClientPropagation or RemainingSubAgentClientPropagation or
            UnboundedRemainingSubAgentClientPropagation))
            throw new InvalidOperationException("subagent_execution_policy_invalid");
    }

    private static string ComputeFingerprint(
        AgentRunConfig? initialRunConfig,
        AgentClientsConfig lockedClients,
        IReadOnlyDictionary<ProviderClientFamily, SubAgentClientSelectionSource> clientSources,
        AgentSecurityRunConfig authority,
        SubAgentClientPropagationState propagation)
    {
        var propagationValue = propagation switch
        {
            NoSubAgentClientPropagation => "none",
            RemainingSubAgentClientPropagation bounded => $"bounded:{bounded.RemainingDepth}",
            UnboundedRemainingSubAgentClientPropagation => "unbounded",
            _ => throw new InvalidOperationException("subagent_execution_policy_invalid")
        };
        var initialRun = initialRunConfig is null
            ? "null"
            : JsonSerializer.Serialize(initialRunConfig, AgentEventJsonContext.Default.AgentRunConfig);
        var clients = JsonSerializer.Serialize(lockedClients, AgentEventJsonContext.Default.AgentClientsConfig);
        var sources = string.Join(',', clientSources.OrderBy(static pair => pair.Key)
            .Select(static pair => $"{pair.Key}:{pair.Value}"));
        var lockedAuthority = JsonSerializer.Serialize(authority, AgentEventJsonContext.Default.AgentSecurityRunConfig);
        var canonical = string.Join('|',
            "hpd.subagent.execution-policy.v2",
            initialRun,
            clients,
            sources,
            lockedAuthority,
            propagationValue);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static AgentClientsConfig CloneClients(AgentClientsConfig source)
    {
        var clone = new AgentClientsConfig { Transport = source.Transport };
        foreach (var family in Enum.GetValues<ProviderClientFamily>())
            if (source.GetFamilyConfig(family) is { } config)
                clone.SetFamilyConfig(family, ProviderClientConfigSnapshot.Clone(config));
        return clone;
    }

    internal static bool HasRuntimeOverride(ProviderClientConfig config) => config switch
    {
        ChatClientConfig value => value.Override is not null,
        RealtimeClientConfig value => value.Override is not null,
        ImageGenerationClientConfig value => value.Override is not null,
        EmbeddingsClientConfig value => value.Override is not null,
        TextToSpeechClientConfig value => value.Override is not null,
        SpeechToTextClientConfig value => value.Override is not null,
        HostedFilesClientConfig value => value.Override is not null,
        VoiceActivityClientConfig => false,
        EndOfTurnClientConfig => false,
        _ => true
    };

    internal static bool HasProviderPayload(ProviderClientConfig config) =>
        config.ProviderConfig is not null || config switch
        {
            ChatClientConfig value => value.ProviderOptions is not null,
            RealtimeClientConfig value => value.ProviderOptions is not null,
            ImageGenerationClientConfig value => value.ProviderOptions is not null,
            EmbeddingsClientConfig value => value.ProviderOptions is not null,
            TextToSpeechClientConfig value => value.ProviderOptions is not null,
            SpeechToTextClientConfig value => value.ProviderOptions is not null,
            HostedFilesClientConfig value => value.ProviderOptions is not null,
            _ => false
        };

    private static void ValidatePortableInitialRun(AgentRunConfig? runConfig)
    {
        if (runConfig is null)
            return;
        if (runConfig.RuntimeMiddleware is { Count: > 0 } ||
            runConfig.RuntimeTools is { Count: > 0 } ||
            runConfig.RuntimeToolMode is not null ||
            runConfig.Evaluations is not null ||
            runConfig.Streaming?.Callback is not null ||
            runConfig.Context?.ToolInstances is { Count: > 0 } ||
            runConfig.Tools?.Additional is { Count: > 0 } ||
            runConfig.BackgroundResponses?.ContinuationToken is not null)
            throw new InvalidOperationException("subagent_run_config_not_portable");
    }
}

/// <summary>Controls client inheritance for HPD MultiAgent nodes and specialized roles.</summary>
public sealed record AgentClientInheritance
{
    public ClientFamilyInheritanceMode Chat { get; init; } = ClientFamilyInheritanceMode.FallbackToParent;
    public ClientFamilyInheritanceMode Realtime { get; init; } = ClientFamilyInheritanceMode.FallbackToParent;
    public ClientFamilyInheritanceMode ImageGeneration { get; init; } = ClientFamilyInheritanceMode.FallbackToParent;
    public ClientFamilyInheritanceMode Embeddings { get; init; } = ClientFamilyInheritanceMode.FallbackToParent;
    public ClientFamilyInheritanceMode TextToSpeech { get; init; } = ClientFamilyInheritanceMode.FallbackToParent;
    public ClientFamilyInheritanceMode SpeechToText { get; init; } = ClientFamilyInheritanceMode.FallbackToParent;
    public ClientFamilyInheritanceMode HostedFiles { get; init; } = ClientFamilyInheritanceMode.FallbackToParent;
    public ClientFamilyInheritanceMode VoiceActivityDetection { get; init; } = ClientFamilyInheritanceMode.UseOwn;
    public ClientFamilyInheritanceMode EndOfTurnDetection { get; init; } = ClientFamilyInheritanceMode.UseOwn;
}

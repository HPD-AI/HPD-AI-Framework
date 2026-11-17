namespace HPD.Environment.Contracts;

/// <summary>
/// Provider-neutral intent for one sandbox session. The Agent Sandbox layer
/// translates this request into Environment operations.
/// </summary>
public sealed record SandboxSessionRequest
{
    /// <summary>Requested filesystem, network, socket, and process policy.</summary>
    public required ProcessIsolationPolicy Isolation { get; init; }

    /// <summary>Process features the selected backend must provide.</summary>
    public SandboxProcessRequirements Process { get; init; } =
        SandboxProcessRequirements.Default;

    /// <summary>Logical workspaces to make available to processes and file tools.</summary>
    public IReadOnlyList<SandboxWorkspaceRequest> Workspaces { get; init; } = [];

    /// <summary>Resource ceilings for the session.</summary>
    public ResourceQuotaPolicy Resources { get; init; } =
        ResourceQuotaPolicy.Default;

    /// <summary>Runtime host and execution-unit lifecycle preferences.</summary>
    public LifecyclePolicy Lifecycle { get; init; } =
        LifecyclePolicy.Default;

    /// <summary>Ordered cleanup requirements for session-owned resources.</summary>
    public CleanupPolicy Cleanup { get; init; } =
        CleanupPolicy.Default;

    /// <summary>Optional provider restriction after backend selection.</summary>
    public ProviderId? PreferredProvider { get; init; }

    /// <summary>Opaque provider hints that cannot alter portable policy semantics.</summary>
    public IReadOnlyList<ProviderExtensionData> ProviderHints { get; init; } = [];
}

/// <summary>Process behavior that must survive selection of a backend.</summary>
public sealed record SandboxProcessRequirements
{
    /// <summary>Default foreground execution requirements.</summary>
    public static SandboxProcessRequirements Default { get; } = new();

    /// <summary>Requires status observation after process start returns.</summary>
    public bool RequireRetainedObservation { get; init; }

    /// <summary>Requires the backend to retain ownership of background processes.</summary>
    public bool RequireBackgroundOwnership { get; init; }

    /// <summary>Requires previously captured output to remain replayable.</summary>
    public bool RequireProcessOutputReplay { get; init; }
}

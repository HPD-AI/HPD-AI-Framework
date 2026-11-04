namespace HPD.Agent.Sandbox;

using HPD.Agent.Middleware;
using HPD.Environment.Contracts;

/// <summary>Identifies the placement mechanism selected for a sandbox session.</summary>
public enum SandboxBackendKind
{
    HostProcess,
    HostContainers,
    VirtualMachine,
}

/// <summary>
/// Creates backend-neutral sandbox sessions after reporting the capabilities
/// that can be enforced by the concrete backend.
/// </summary>
public interface ISandboxBackendFactory
{
    /// <summary>Gets the placement mechanism implemented by this factory.</summary>
    SandboxBackendKind Kind { get; }

    /// <summary>Queries provider and sandbox capabilities for a prospective session.</summary>
    ValueTask<SandboxBackendCapabilities> GetCapabilitiesAsync(
        SandboxCapabilitiesQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a session or fails closed when its required capabilities cannot
    /// be enforced.
    /// </summary>
    ValueTask<ISandboxSession> CreateSessionAsync(
        SandboxSessionRequest request,
        CancellationToken cancellationToken = default);
}

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

/// <summary>
/// A logical workspace plus the backend execution path where its content must
/// be visible.
/// </summary>
public sealed record SandboxWorkspaceRequest
{
    /// <summary>
    /// Trusted content source represented by the logical workspace root.
    /// Providers must reject sources they cannot expose at the requested
    /// execution path.
    /// </summary>
    public required ContentSelector Source { get; init; }

    /// <summary>Logical root metadata exposed to file-aware tools.</summary>
    public required WorkspaceRootBinding Root { get; init; }

    /// <summary>Trusted backend path where the projected workspace is required.</summary>
    public required WorkspaceExecutionPath ExecutionPath { get; init; }

    /// <summary>Role of the content projection within the session.</summary>
    public ContentProjectionRole Role { get; init; } =
        ContentProjectionRole.Workspace;

    /// <summary>Requested synchronization behavior; unsupported modes fail closed.</summary>
    public SyncPolicy SyncPolicy { get; init; } = SyncPolicy.None;

    /// <summary>Requested content finalization behavior.</summary>
    public FinalizationPolicy FinalizationPolicy { get; init; } =
        FinalizationPolicy.Explicit;

    /// <summary>Security requirements for exposing or mutating source content.</summary>
    public ContentProjectionSecurityPolicy SecurityPolicy { get; init; } =
        ContentProjectionSecurityPolicy.Default;
}

/// <summary>
/// Agent-facing sandbox session that binds process execution and workspace file
/// access to one selected Environment execution unit.
/// </summary>
public interface ISandboxSession : IAsyncDisposable
{
    /// <summary>Selected placement mechanism.</summary>
    SandboxBackendKind Backend { get; }

    /// <summary>Single authorized binding used by command-backed tools.</summary>
    RuntimeProcessExecutionBinding Execution { get; }

    /// <summary>Workspace binding shared by file tools and trusted tooling services.</summary>
    RuntimeWorkspaceBinding Workspace { get; }

    /// <summary>Capabilities used to admit and describe this session.</summary>
    SandboxBackendCapabilities Capabilities { get; }
}

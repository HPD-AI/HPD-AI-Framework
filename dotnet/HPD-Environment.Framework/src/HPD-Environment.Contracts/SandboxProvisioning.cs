namespace HPD.Environment.Contracts;

/// <summary>Metadata for one logical root owned by a sandbox session.</summary>
public sealed record WorkspaceRootBinding
{
    /// <summary>Stable model-facing root identifier.</summary>
    public required WorkspaceRootId Id { get; init; }

    /// <summary>Human-readable name used in diagnostics.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Access granted to model-facing file operations.</summary>
    public AccessMode Access { get; init; } = AccessMode.ReadOnly;

    /// <summary>Logical default directory for commands associated with this root.</summary>
    public required WorkspacePath DefaultExecutionPath { get; init; }

    /// <summary>Content instance and generation exposed through this root.</summary>
    public required WorkspaceContentIdentity Content { get; init; }

    /// <summary>Path behavior that the backend can guarantee.</summary>
    public required WorkspacePathSemantics PathSemantics { get; init; }
}

/// <summary>Provider inputs for preparing one sandbox session.</summary>
public sealed record SandboxProvisioningRequest
{
    public required ProcessIsolationPolicy Isolation { get; init; }
    public IReadOnlyList<SandboxWorkspaceRequest> Workspaces { get; init; } = [];
    public ResourceQuotaPolicy Resources { get; init; } = ResourceQuotaPolicy.Default;
    public LifecyclePolicy Lifecycle { get; init; } = LifecyclePolicy.Default;
}

/// <summary>
/// A logical workspace plus the backend execution path where its content must
/// be visible.
/// </summary>
public sealed record SandboxWorkspaceRequest
{
    /// <summary>Stage a writable host directory and copy changes back at finalization.</summary>
    public bool CopyInOut { get; init; }

    public static SandboxWorkspaceRequest HostDirectory(
        string path, AccessMode access = AccessMode.ReadWrite,
        string rootId = "workspace", string? executionPath = null)
    {
        string fullPath = Path.GetFullPath(path);
        var id = new WorkspaceRootId(rootId);
        return new SandboxWorkspaceRequest
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.HostPath,
                HostPath = new HostPathSelection(new HostPath(fullPath)),
            },
            Root = new WorkspaceRootBinding
            {
                Id = id,
                DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath)),
                Access = access,
                DefaultExecutionPath = new WorkspacePath(id, string.Empty),
                Content = new WorkspaceContentIdentity($"host-directory:{fullPath}"),
                PathSemantics = new WorkspacePathSemantics(OperatingSystem.IsLinux(), SymlinkEvaluationPolicy.DenySymlinks),
            },
            ExecutionPath = new WorkspaceExecutionPath(executionPath ?? fullPath),
            SecurityPolicy = ContentProjectionSecurityPolicy.Default with
            {
                AllowHostPathSource = true,
                AllowDirectSourceMutation = access == AccessMode.ReadWrite,
                ReadOnlyEnforcement = ReadOnlyEnforcementPolicy.BestEffort,
            },
        };
    }

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

/// <summary>Provider-specific setup for the requested environment.</summary>
public sealed record SandboxProvisioningPlan(
    RuntimeHostSpec Host,
    SandboxEnforcementLocation EnforcementLocation,
    Func<ProviderCapabilityReport, ProviderCapabilityReport>? RefineCapabilities = null)
{
    public IReadOnlyList<SandboxWorkspaceRequest>? Workspaces { get; init; }
    public Func<ResourceRef<RuntimeHost>, ExecutionUnitSpec>? CreateUnit { get; init; }
    public ProviderActivationSpec? Activation { get; init; }
    public Func<IWorkspaceFileStore, CancellationToken, Task>? InitializeWorkspace { get; init; }
    public Func<IWorkspaceFileStore, Task>? FinalizeWorkspace { get; init; }
}

/// <summary>Optional provider contract for preparing a sandbox session.</summary>
public interface ISandboxProvisioningProvider : IProviderModule
{
    /// <summary>Creates a provider instance whose state is owned by one sandbox session.</summary>
    ISandboxProvisioningProvider CreateSessionProvider();

    /// <summary>
    /// Prepares provider-owned resources and returns the host plan for one request.
    /// Any resources acquired during preparation belong to the provider module.
    /// </summary>
    SandboxProvisioningPlan PrepareSandbox(
        SandboxProvisioningRequest request,
        CancellationToken cancellationToken = default);
}

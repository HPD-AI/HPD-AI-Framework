namespace HPD.Agent.Sandbox;

using HPD.Environment.Contracts;

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

/// <summary>
/// Session-scoped workspace capability shared by coding tools, process working
/// directories, language servers, and debug adapters.
/// </summary>
public sealed record RuntimeWorkspaceBinding
{
    /// <summary>File operations over the logical workspace content.</summary>
    public required IWorkspaceFileStore Files { get; init; }

    /// <summary>Trusted mapping to backend execution paths and file URIs.</summary>
    public required IWorkspacePathMapper Paths { get; init; }

    /// <summary>Default logical directory for command execution.</summary>
    public required WorkspacePath WorkingDirectory { get; init; }

    /// <summary>Logical roots available in this session.</summary>
    public required IReadOnlyList<WorkspaceRootBinding> Roots { get; init; }

    /// <summary>Content identity that must match the process and projection.</summary>
    public required WorkspaceContentIdentity Content { get; init; }
}

namespace HPD.Agent.Sandbox;

using HPD.Environment.Contracts;

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

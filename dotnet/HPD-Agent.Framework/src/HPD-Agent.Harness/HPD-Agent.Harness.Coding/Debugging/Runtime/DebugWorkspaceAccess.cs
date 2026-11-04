namespace HPD.Agent.ToolHarness.Coding.Debugging;

using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;

/// <summary>
/// Resolves debugger inputs through the session workspace and translates them
/// to effective paths only at trusted backend boundaries.
/// </summary>
internal static class DebugWorkspaceAccess
{
    /// <summary>
    /// Resolves a model-facing path against the binding's working directory.
    /// Model-facing debug inputs use the same logical address space as file
    /// tools, including <c>@root-id/relative/path</c> addresses.
    /// </summary>
    public static WorkspacePath Resolve(
        RuntimeWorkspaceBinding workspace,
        string path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return CodingWorkspaceAccess.Resolve(workspace, path);
    }

    /// <summary>Resolves a path relative to an explicit logical directory.</summary>
    public static WorkspacePath ResolveRelative(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath directory,
        string path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.StartsWith('@'))
        {
            return Resolve(workspace, path);
        }

        string relative = directory.RelativePath.Length == 0
            ? path
            : directory.RelativePath.TrimEnd('/') + "/" + path;
        return new WorkspacePath(directory.RootId, Normalize(relative));
    }

    /// <summary>
    /// Resolves a lexical relative path from one effective workspace directory
    /// and maps the result through the session mapper. This supports solution
    /// references that cross explicitly configured logical roots.
    /// </summary>
    public static WorkspacePath ResolveEffectiveRelative(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath directory,
        string path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.StartsWith('@'))
        {
            return Resolve(workspace, path);
        }

        string combined =
            ToEffective(workspace, directory).TrimEnd('/', '\\') +
            "/" +
            path.Replace('\\', '/');
        string canonical = CanonicalizeEffectivePath(combined);
        return workspace.Paths.TryFromExecutionPath(
                new WorkspaceExecutionPath(canonical)) ??
            throw new UnauthorizedAccessException(
                "The relative path resolves outside the configured workspace roots.");
    }

    /// <summary>Maps an effective backend path back into the session workspace.</summary>
    public static WorkspacePath FromEffective(
        RuntimeWorkspaceBinding workspace,
        string path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return workspace.Paths.TryFromExecutionPath(
                new WorkspaceExecutionPath(path)) ??
            throw new UnauthorizedAccessException(
                "The effective debugger path is outside the session workspace.");
    }

    /// <summary>Maps an effective backend path when it belongs to the workspace.</summary>
    public static WorkspacePath? TryFromEffective(
        RuntimeWorkspaceBinding workspace,
        string? path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return string.IsNullOrWhiteSpace(path)
            ? null
            : workspace.Paths.TryFromExecutionPath(
                new WorkspaceExecutionPath(path));
    }

    /// <summary>Maps a logical path to the path observed by backend tooling.</summary>
    public static string ToEffective(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return workspace.Paths.ToExecutionPath(path).Value;
    }

    /// <summary>Gets the parent logical path or null at a workspace root.</summary>
    public static WorkspacePath? Parent(WorkspacePath path)
    {
        if (path.RelativePath.Length == 0)
        {
            return null;
        }

        int separator = path.RelativePath.LastIndexOf('/');
        return new WorkspacePath(
            path.RootId,
            separator < 0
                ? string.Empty
                : path.RelativePath[..separator]);
    }

    /// <summary>Gets the parent of a POSIX or Windows effective path.</summary>
    public static string? EffectiveParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.TrimEnd('/', '\\');
        int separator = Math.Max(
            normalized.LastIndexOf('/'),
            normalized.LastIndexOf('\\'));
        if (separator < 0)
        {
            return null;
        }
        if (separator == 0)
        {
            return normalized[..1];
        }
        if (separator == 2 &&
            char.IsLetter(normalized[0]) &&
            normalized[1] == ':' &&
            normalized[2] is '/' or '\\')
        {
            return normalized[..3];
        }
        return normalized[..separator];
    }

    /// <summary>Gets a logical root path for the supplied root identifier.</summary>
    public static WorkspacePath Root(WorkspaceRootId rootId) =>
        new(rootId, string.Empty);

    /// <summary>Validates that the root permits the requested access.</summary>
    public static void DemandAccess(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath path,
        AccessMode required)
    {
        WorkspaceRootBinding root = CodingWorkspaceAccess.GetRoot(
            workspace,
            path);
        bool granted = required == AccessMode.ReadOnly
            ? root.Access is AccessMode.ReadOnly or AccessMode.ReadWrite
            : root.Access == AccessMode.ReadWrite;
        if (!granted)
        {
            throw new UnauthorizedAccessException(
                $"Workspace root '{path.RootId.Value}' does not grant {required} access.");
        }
    }

    /// <summary>Reads one complete text file within an explicit byte bound.</summary>
    public static async ValueTask<WorkspaceTextSnapshot> ReadTextAsync(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        WorkspaceTextSnapshot snapshot =
            await workspace.Files.ReadTextAsync(
                path,
                new WorkspaceTextReadOptions
                {
                    StartLine = 1,
                    MaxLines = null,
                    MaxBytes = maximumBytes,
                },
                cancellationToken).ConfigureAwait(false) ??
            throw new FileNotFoundException(
                $"Workspace file '{path}' does not exist.",
                path.ToString());
        if (snapshot.Truncated)
        {
            throw new DebugStartPlanningException(
                "debug_source_too_large",
                "A debug planning input exceeds its trusted read bound.");
        }
        return snapshot;
    }

    private static string Normalize(string path)
    {
        var segments = new List<string>();
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                throw new UnauthorizedAccessException(
                    "Workspace path traversal is denied.");
            }
            segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    private static string CanonicalizeEffectivePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        string prefix;
        int segmentStart;
        if (normalized.Length >= 3 &&
            char.IsLetter(normalized[0]) &&
            normalized[1] == ':' &&
            normalized[2] == '/')
        {
            prefix = normalized[..3];
            segmentStart = 3;
        }
        else if (normalized.StartsWith('/'))
        {
            prefix = "/";
            segmentStart = 1;
        }
        else
        {
            prefix = string.Empty;
            segmentStart = 0;
        }

        var segments = new List<string>();
        foreach (string segment in normalized[segmentStart..].Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new UnauthorizedAccessException(
                        "Workspace path traversal is denied.");
                }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return prefix + string.Join('/', segments);
    }
}

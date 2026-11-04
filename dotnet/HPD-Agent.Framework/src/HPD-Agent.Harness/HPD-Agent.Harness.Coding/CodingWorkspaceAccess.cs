namespace HPD.Agent.ToolHarness.Coding;

using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;

/// <summary>
/// Resolves model-facing paths against the session workspace binding. No
/// physical path is returned to tool bodies; trusted execution paths are only
/// accepted as inputs and mapped back to logical addresses.
/// </summary>
internal static class CodingWorkspaceAccess
{
    /// <summary>Gets the session binding when a sandbox workspace is active.</summary>
    public static bool TryGet(
        FunctionExecutionContext? context,
        out RuntimeWorkspaceBinding binding)
    {
        binding = null!;
        return context is not null &&
            context.RuntimeCapabilities.TryGet(
                out binding) &&
            binding.Roots.Count > 0;
    }

    /// <summary>Resolves a tool path to a logical workspace address.</summary>
    public static WorkspacePath Resolve(
        RuntimeWorkspaceBinding binding,
        string path)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Workspace path is required.",
                nameof(path));
        }

        string trimmed = path.Trim();
        if (trimmed.StartsWith('@'))
        {
            int separator = trimmed.IndexOf('/');
            string rootId = separator < 0
                ? trimmed[1..]
                : trimmed[1..separator];
            string relative = separator < 0
                ? string.Empty
                : trimmed[(separator + 1)..];
            if (!binding.Roots.Any(root =>
                    string.Equals(
                        root.Id.Value,
                        rootId,
                        StringComparison.Ordinal)))
            {
                throw new UnauthorizedAccessException(
                    $"Workspace root '{rootId}' is not available in this session.");
            }
            return new WorkspacePath(
                new WorkspaceRootId(rootId),
                Normalize(relative));
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "UNC and network-style paths are not workspace addresses.");
        }
        if (Path.IsPathRooted(trimmed))
        {
            if (binding.Paths.TryFromExecutionPath(
                    new WorkspaceExecutionPath(trimmed)) is { } logical)
            {
                return logical;
            }
            throw new UnauthorizedAccessException(
                "Absolute execution paths are not model-facing workspace addresses.");
        }

        WorkspacePath working = binding.WorkingDirectory;
        string combined = working.RelativePath.Length == 0
            ? trimmed
            : working.RelativePath + "/" + trimmed;
        return new WorkspacePath(
            working.RootId,
            Normalize(combined));
    }

    /// <summary>Gets root metadata for an access-checked logical path.</summary>
    public static WorkspaceRootBinding GetRoot(
        RuntimeWorkspaceBinding binding,
        WorkspacePath path)
    {
        // Session creation rejects duplicate root identifiers, so the first
        // matching root is the single authoritative access policy.
        WorkspaceRootBinding root = binding.Roots.FirstOrDefault(
                candidate => candidate.Id.Equals(path.RootId)) ??
            throw new UnauthorizedAccessException(
                $"Workspace root '{path.RootId.Value}' is not available in this session.");
        return root;
    }

    /// <summary>Gets a stable model-facing display path.</summary>
    public static string Display(
        RuntimeWorkspaceBinding binding,
        WorkspacePath path) =>
        binding.Roots.Count == 1
            ? path.RelativePath.Length == 0
                ? "."
                : path.RelativePath
            : path.ToString();

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
}

using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;
using Microsoft.Extensions.FileSystemGlobbing;

namespace HPDOS.ToolHarnesses.Middleware;

/// <summary>
/// Trusted filesystem operations for language-server discovery. All paths are
/// resolved through the session workspace and translated only at the adapter
/// boundary.
/// </summary>
internal static class LanguageServerWorkspaceAccess
{
    public static WorkspacePath ResolvePath(
        RuntimeWorkspaceBinding workspace,
        string path) =>
        workspace.Paths.TryFromExecutionPath(
            new WorkspaceExecutionPath(path)) ??
        HPD.Agent.ToolHarness.Coding.CodingWorkspaceAccess.Resolve(
            workspace,
            path);

    public static WorkspacePath ResolveRelative(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath directory,
        string path)
    {
        if (Path.IsPathRooted(path) || path.StartsWith('@'))
        {
            return ResolvePath(workspace, path);
        }
        var segments = new List<string>();
        string combined = directory.RelativePath.Length == 0
            ? path
            : directory.RelativePath.TrimEnd('/') + "/" + path;
        foreach (string segment in combined.Replace('\\', '/').Split('/'))
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
                        "The language-server tool path escapes its workspace root.");
                }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return new WorkspacePath(
            directory.RootId,
            string.Join('/', segments));
    }

    public static async ValueTask<WorkspacePath> ResolveDirectoryAsync(
        RuntimeWorkspaceBinding workspace,
        string path,
        CancellationToken cancellationToken)
    {
        WorkspacePath resolved = ResolvePath(workspace, path);
        return await workspace.Files.GetInfoAsync(
                resolved,
                cancellationToken).ConfigureAwait(false) is
            { Kind: WorkspaceEntryKind.File }
                ? Parent(resolved) ??
                    new WorkspacePath(
                        resolved.RootId,
                        string.Empty)
                : resolved;
    }

    public static async ValueTask<WorkspacePath?> ResolveAsync(
        RuntimeWorkspaceBinding workspace,
        string path,
        CancellationToken cancellationToken)
    {
        WorkspacePath logical =
            workspace.Paths.TryFromExecutionPath(
                new WorkspaceExecutionPath(path)) ??
            HPD.Agent.ToolHarness.Coding.CodingWorkspaceAccess.Resolve(
                workspace,
                path);
        return await workspace.Files.GetInfoAsync(
                logical,
                cancellationToken).ConfigureAwait(false) is null
            ? null
            : logical;
    }

    public static async ValueTask<bool> MarkerExistsAsync(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath directory,
        string marker,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(marker) ||
            marker.Contains('/') ||
            marker.Contains('\\') ||
            marker.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(marker))
        {
            throw new ArgumentException(
                "Language-server markers must be safe names or globs.",
                nameof(marker));
        }

        if (!marker.Contains('*') && !marker.Contains('?'))
        {
            WorkspacePath candidate =
                ResolveRelative(
                    workspace,
                    directory,
                    marker);
            return await workspace.Files.GetInfoAsync(
                    candidate,
                    cancellationToken).ConfigureAwait(false) is not null;
        }

        WorkspaceListResultSet listing =
            await workspace.Files.ListAsync(
                directory,
                new WorkspaceListOptions
                {
                    Recursive = false,
                    MaxEntries = 256,
                },
                cancellationToken).ConfigureAwait(false);
        var matcher = new Matcher(
            workspace.Roots.Single(root =>
                    root.Id.Equals(directory.RootId))
                .PathSemantics.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(marker);
        return listing.Entries.Any(entry =>
        {
            int separator = entry.Path.RelativePath.LastIndexOf('/');
            string leaf = entry.Path.RelativePath[
                (separator + 1)..];
            return matcher.Match(leaf).HasMatches;
        });
    }

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

    public static string ToExecutionPath(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath path) =>
        workspace.Paths.ToExecutionPath(path).Value;
}

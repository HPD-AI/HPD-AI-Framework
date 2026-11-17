using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Sandbox;
using HPD.Agent.Security;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Environment.Contracts;
using Microsoft.Extensions.FileSystemGlobbing;

namespace HPD.Agent.ToolHarness.Coding;

public sealed record WorkspaceRootMarkerResolution(
    string WorkspaceRoot,
    string? ProjectRoot,
    IReadOnlySet<string> MatchedMarkers,
    IReadOnlyList<string> MatchedPaths,
    string Fingerprint);

public interface IWorkspaceRootMarkerResolver
{
    /// <summary>
    /// Resolves project markers through a session workspace file store.
    /// </summary>
    ValueTask<WorkspaceRootMarkerResolution> ResolveAsync(
        RuntimeWorkspaceBinding workspace,
        string startingExecutionPath,
        IReadOnlyCollection<string> markers,
        CancellationToken cancellationToken = default);

}

public sealed class WorkspaceRootMarkerResolver : IWorkspaceRootMarkerResolver
{
    private const int MaximumMarkers = 128;
    private const int MaximumMatches = 256;
    private const int MaximumDepth = 64;

    /// <inheritdoc />
    public async ValueTask<WorkspaceRootMarkerResolution> ResolveAsync(
        RuntimeWorkspaceBinding workspace,
        string startingExecutionPath,
        IReadOnlyCollection<string> markers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(startingExecutionPath);
        ArgumentNullException.ThrowIfNull(markers);
        if (markers.Count > MaximumMarkers)
        {
            throw new ArgumentOutOfRangeException(nameof(markers));
        }

        WorkspacePath canonicalStart =
            DebugWorkspaceAccess.FromEffective(
                workspace,
                startingExecutionPath);
        WorkspaceFileInfo startInfo =
            await workspace.Files.GetInfoAsync(
                canonicalStart,
                cancellationToken).ConfigureAwait(false) ??
            throw new DebugStartPlanningException(
                "debug_target_not_found",
                "The selected debug target is not visible in the workspace.");
        WorkspacePath directory = startInfo.Kind ==
            WorkspaceEntryKind.Directory
                ? canonicalStart
                : DebugWorkspaceAccess.Parent(canonicalStart) ??
                    DebugWorkspaceAccess.Root(canonicalStart.RootId);
        WorkspacePath owningRoot =
            DebugWorkspaceAccess.Root(canonicalStart.RootId);
        string[] normalizedMarkers = markers
            .Where(marker => !string.IsNullOrWhiteSpace(marker))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(marker => marker, StringComparer.Ordinal)
            .ToArray();
        var matchedMarkers = new HashSet<string>(
            StringComparer.Ordinal);
        var matchedPaths = new List<string>();
        string? projectRoot = null;

        for (int depth = 0; depth < MaximumDepth; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool directoryMatched = false;
            foreach (string marker in normalizedMarkers)
            {
                foreach (WorkspacePath path in await MatchesAsync(
                             workspace,
                             directory,
                             marker,
                             cancellationToken).ConfigureAwait(false))
                {
                    if (matchedPaths.Count >= MaximumMatches)
                    {
                        throw new InvalidOperationException(
                            "Workspace marker discovery exceeded its match limit.");
                    }
                    matchedMarkers.Add(marker);
                    matchedPaths.Add(
                        DebugWorkspaceAccess.ToEffective(
                            workspace,
                            path));
                    directoryMatched = true;
                }
            }

            if (directoryMatched && projectRoot is null)
            {
                projectRoot = DebugWorkspaceAccess.ToEffective(
                    workspace,
                    directory);
            }
            if (directory == owningRoot)
            {
                break;
            }
            directory = DebugWorkspaceAccess.Parent(directory) ??
                owningRoot;
        }

        matchedPaths.Sort(StringComparer.Ordinal);
        string owningRootExecutionPath =
            DebugWorkspaceAccess.ToEffective(workspace, owningRoot);
        string fingerprintText = string.Join('\n', matchedPaths.Select(path =>
            RelativeExecutionPath(owningRootExecutionPath, path)));
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(fingerprintText)))
            .ToLowerInvariant();
        return new WorkspaceRootMarkerResolution(
            owningRootExecutionPath,
            projectRoot,
            matchedMarkers,
            matchedPaths,
            fingerprint);
    }

    private static async ValueTask<
        IReadOnlyList<WorkspacePath>> MatchesAsync(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath directory,
        string marker,
        CancellationToken cancellationToken)
    {
        if (marker.Contains('/') ||
            marker.Contains('\\') ||
            marker.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(marker))
        {
            throw new ArgumentException(
                "Workspace root markers must be safe names or globs.",
                nameof(marker));
        }

        bool isGlob = marker.Contains('*') ||
            marker.Contains('?');
        if (!isGlob)
        {
            WorkspacePath candidate = DebugWorkspaceAccess.ResolveRelative(
                workspace,
                directory,
                marker);
            return await workspace.Files.GetInfoAsync(
                    candidate,
                    cancellationToken).ConfigureAwait(false) is null
                ? []
                : [candidate];
        }

        WorkspaceListResultSet listing =
            await workspace.Files.ListAsync(
                directory,
                new WorkspaceListOptions
                {
                    Recursive = false,
                    MaxEntries = MaximumMatches + 1,
                },
                cancellationToken).ConfigureAwait(false);
        var matcher = new Matcher(
            workspace.Roots.Single(root =>
                    root.Id.Equals(directory.RootId))
                .PathSemantics.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(marker);
        return listing.Entries
            .Where(entry =>
            {
                string relative = entry.Path.RelativePath;
                string leaf = relative[
                    (relative.LastIndexOf('/') + 1)..];
                return matcher.Match(leaf).HasMatches;
            })
            .Select(entry => entry.Path)
            .OrderBy(path => path.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string RelativeExecutionPath(
        string root,
        string path)
    {
        string normalizedRoot = root.TrimEnd('/');
        if (path.StartsWith(
                normalizedRoot + "/",
                StringComparison.Ordinal))
        {
            return path[(normalizedRoot.Length + 1)..];
        }
        throw new UnauthorizedAccessException(
            "Workspace marker evidence is outside its owning root.");
    }


}

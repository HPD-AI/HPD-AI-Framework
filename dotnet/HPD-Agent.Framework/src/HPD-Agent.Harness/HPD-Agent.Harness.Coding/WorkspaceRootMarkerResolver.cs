using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.ToolHarness.Coding;

public sealed record WorkspaceRootMarkerResolution(
    string WorkspaceRoot,
    string? ProjectRoot,
    IReadOnlySet<string> MatchedMarkers,
    IReadOnlyList<string> MatchedPaths,
    string Fingerprint);

public interface IWorkspaceRootMarkerResolver
{
    ValueTask<WorkspaceRootMarkerResolution> ResolveAsync(
        AgentWorkspace workspace,
        string startingPath,
        IReadOnlyCollection<string> markers,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceRootMarkerResolver : IWorkspaceRootMarkerResolver
{
    private const int MaximumMarkers = 128;
    private const int MaximumMatches = 256;
    private const int MaximumDepth = 64;

    public ValueTask<WorkspaceRootMarkerResolution> ResolveAsync(
        AgentWorkspace workspace,
        string startingPath,
        IReadOnlyCollection<string> markers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(startingPath);
        ArgumentNullException.ThrowIfNull(markers);
        if (markers.Count > MaximumMarkers)
            throw new ArgumentOutOfRangeException(nameof(markers));

        var canonicalStart = File.Exists(startingPath)
            ? workspace.ResolveWorkspacePath(startingPath)
            : workspace.ResolveDirectory(startingPath);
        var owningRoot = workspace.GetOwningRoot(canonicalStart)
            ?? throw new InvalidOperationException("The marker start path is outside the workspace.");
        var directory = File.Exists(canonicalStart)
            ? Path.GetDirectoryName(canonicalStart)!
            : canonicalStart;
        var normalizedMarkers = markers
            .Where(marker => !string.IsNullOrWhiteSpace(marker))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(marker => marker, StringComparer.Ordinal)
            .ToArray();
        var matchedMarkers = new HashSet<string>(StringComparer.Ordinal);
        var matchedPaths = new List<string>();
        string? projectRoot = null;

        for (var depth = 0; depth < MaximumDepth && IsInside(directory, owningRoot.Path); depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryMatched = false;
            foreach (var marker in normalizedMarkers)
            {
                foreach (var path in Matches(directory, marker))
                {
                    if (matchedPaths.Count >= MaximumMatches)
                        throw new InvalidOperationException("Workspace marker discovery exceeded its match limit.");
                    matchedMarkers.Add(marker);
                    matchedPaths.Add(Path.GetFullPath(path));
                    directoryMatched = true;
                }
            }
            if (directoryMatched && projectRoot is null)
                projectRoot = directory;
            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, directory, StringComparison.Ordinal))
                break;
            directory = parent;
        }

        matchedPaths.Sort(StringComparer.Ordinal);
        var fingerprintText = string.Join('\n', matchedPaths.Select(path =>
            Path.GetRelativePath(owningRoot.Path, path).Replace(Path.DirectorySeparatorChar, '/')));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintText))).ToLowerInvariant();
        return ValueTask.FromResult(new WorkspaceRootMarkerResolution(
            owningRoot.Path,
            projectRoot,
            matchedMarkers,
            matchedPaths,
            fingerprint));
    }

    private static IEnumerable<string> Matches(string directory, string marker)
    {
        if (marker.Contains(Path.DirectorySeparatorChar) ||
            marker.Contains(Path.AltDirectorySeparatorChar) ||
            marker.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(marker))
            throw new ArgumentException("Workspace root markers must be safe names or globs.", nameof(marker));
        try
        {
            return marker.Contains('*', StringComparison.Ordinal) || marker.Contains('?', StringComparison.Ordinal)
                ? Directory.EnumerateFileSystemEntries(directory, marker)
                    .OrderBy(path => path, StringComparer.Ordinal).ToArray()
                : File.Exists(Path.Combine(directory, marker)) || Directory.Exists(Path.Combine(directory, marker))
                    ? [Path.Combine(directory, marker)]
                    : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsInside(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ||
            (!Path.IsPathRooted(relative) &&
             !relative.Equals("..", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}

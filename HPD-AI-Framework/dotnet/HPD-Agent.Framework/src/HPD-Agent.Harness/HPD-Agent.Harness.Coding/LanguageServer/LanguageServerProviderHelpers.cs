namespace HPDOS.ToolHarnesses.Middleware;

public sealed class StaticCommandLanguageServerProvider(
    IReadOnlyList<string> markers,
    string executable,
    IReadOnlyList<string>? arguments = null,
    IReadOnlyList<string>? excludeMarkers = null,
    IReadOnlyDictionary<string, string>? environment = null) : ILanguageServerProvider
{
    private readonly IReadOnlyList<string> _markers = markers;
    private readonly IReadOnlyList<string> _excludeMarkers = excludeMarkers ?? [];

    public ValueTask<string?> ResolveRootAsync(
        LanguageServerRootContext context,
        CancellationToken cancellationToken = default)
    {
        var directory = File.Exists(context.Path)
            ? Path.GetDirectoryName(context.Path)
            : context.Path;

        while (!string.IsNullOrEmpty(directory) && IsInsideWorkspace(directory, context.WorkspaceRoot))
        {
            if (_excludeMarkers.Any(marker => MarkerExists(directory, marker)))
                return ValueTask.FromResult<string?>(null);

            if (_markers.Any(marker => MarkerExists(directory, marker)))
                return ValueTask.FromResult<string?>(directory);

            directory = Path.GetDirectoryName(directory);
        }

        return ValueTask.FromResult<string?>(context.WorkspaceRoot);
    }

    public async ValueTask<LanguageServerLaunchDescriptor?> ResolveLaunchAsync(
        LanguageServerLaunchContext context,
        CancellationToken cancellationToken = default)
    {
        var resolved = await context.ToolResolver
            .FindExecutableAsync(executable, context.Root, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(resolved))
            return null;

        return new LanguageServerLaunchDescriptor
        {
            FileName = resolved,
            Arguments = arguments ?? [],
            Environment = environment ?? new Dictionary<string, string>(StringComparer.Ordinal),
            WorkingDirectory = context.Root
        };
    }

    public ValueTask<LanguageServerInitialization> CreateInitializationAsync(
        LanguageServerInitializationContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new LanguageServerInitialization());

    private static bool IsInsideWorkspace(string directory, string workspaceRoot)
    {
        var relative = Path.GetRelativePath(workspaceRoot, directory);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    private static bool MarkerExists(string directory, string marker)
    {
        if (marker.Contains('*', StringComparison.Ordinal) || marker.Contains('?', StringComparison.Ordinal))
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(directory, marker).Any();
            }
            catch
            {
                return false;
            }
        }

        return File.Exists(Path.Combine(directory, marker)) || Directory.Exists(Path.Combine(directory, marker));
    }
}

public sealed class LanguageServerToolResolver : ILanguageServerToolResolver
{
    public ValueTask<string?> FindExecutableAsync(
        string name,
        string root,
        CancellationToken cancellationToken = default)
    {
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            return ValueTask.FromResult(File.Exists(name) ? name : null);

        var localBin = FindLocalBinCore(name, root);
        if (localBin is not null)
            return ValueTask.FromResult<string?>(localBin);

        foreach (var directory in SplitPath(System.Environment.GetEnvironmentVariable("PATH")))
        {
            var candidate = FindExecutableInDirectory(directory, name);
            if (candidate is not null)
                return ValueTask.FromResult<string?>(candidate);
        }

        return ValueTask.FromResult<string?>(null);
    }

    public ValueTask<string?> FindNodeModuleAsync(
        string modulePath,
        string root,
        CancellationToken cancellationToken = default)
    {
        var directory = root;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, "node_modules", modulePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return ValueTask.FromResult<string?>(candidate);

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory)
                break;

            directory = parent;
        }

        return ValueTask.FromResult<string?>(null);
    }

    public ValueTask<string?> FindLocalBinAsync(
        string name,
        string root,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(FindLocalBinCore(name, root));

    private static string? FindLocalBinCore(string name, string root)
    {
        var directory = root;
        while (!string.IsNullOrEmpty(directory))
        {
            var binDirectory = Path.Combine(directory, "node_modules", ".bin");
            var candidate = FindExecutableInDirectory(binDirectory, name);
            if (candidate is not null)
                return candidate;

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory)
                break;

            directory = parent;
        }

        return null;
    }

    private static IEnumerable<string> SplitPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        foreach (var part in path.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
        }
    }

    private static string? FindExecutableInDirectory(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        foreach (var candidateName in GetExecutableCandidateNames(name))
        {
            var candidate = Path.Combine(directory, candidateName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetExecutableCandidateNames(string name)
    {
        yield return name;

        if (!OperatingSystem.IsWindows())
            yield break;

        var extension = Path.GetExtension(name);
        if (!string.IsNullOrEmpty(extension))
            yield break;

        var pathExt = System.Environment.GetEnvironmentVariable("PATHEXT");
        foreach (var item in string.IsNullOrWhiteSpace(pathExt)
            ? [".COM", ".EXE", ".BAT", ".CMD"]
            : pathExt.Split(';'))
        {
            if (!string.IsNullOrWhiteSpace(item))
                yield return name + item;
        }
    }
}

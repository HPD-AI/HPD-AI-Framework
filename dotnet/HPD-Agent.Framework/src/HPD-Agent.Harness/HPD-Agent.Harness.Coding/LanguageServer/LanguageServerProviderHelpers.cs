namespace HPDOS.ToolHarnesses.Middleware;

using HPD.Environment.Contracts;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using System.Text;

public sealed class StaticCommandLanguageServerProvider(
    IReadOnlyList<string> markers,
    string executable,
    IReadOnlyList<string>? arguments = null,
    IReadOnlyList<string>? excludeMarkers = null,
    IReadOnlyDictionary<string, string>? environment = null) : ILanguageServerProvider
{
    private readonly IReadOnlyList<string> _markers = markers;
    private readonly IReadOnlyList<string> _excludeMarkers = excludeMarkers ?? [];

    public string ConfigurationIdentity => string.Join("|", new[]
    {
        Encode(_markers),
        Encode([executable]),
        Encode(arguments ?? []),
        Encode(excludeMarkers ?? []),
        Encode((environment ?? new Dictionary<string, string>())
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key.Length}:{pair.Key}{pair.Value.Length}:{pair.Value}"))
    });

    public async ValueTask<string?> ResolveRootAsync(
        LanguageServerRootContext context,
        CancellationToken cancellationToken = default)
    {
        WorkspacePath start =
            await LanguageServerWorkspaceAccess.ResolveAsync(
                context.Workspace,
                context.Path,
                cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "The language-server document is outside the session workspace.");
        WorkspaceFileInfo info =
            await context.Workspace.Files.GetInfoAsync(
                start,
                cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "The language-server document disappeared.");
        WorkspacePath? directory = info.Kind ==
            WorkspaceEntryKind.Directory
                ? start
                : LanguageServerWorkspaceAccess.Parent(start);
        WorkspacePath workspaceRoot =
            HPD.Agent.ToolHarness.Coding.CodingWorkspaceAccess.Resolve(
                context.Workspace,
                context.WorkspaceRoot);

        while (directory is { } current)
        {
            foreach (string marker in _excludeMarkers)
            {
                if (await LanguageServerWorkspaceAccess
                        .MarkerExistsAsync(
                            context.Workspace,
                            current,
                            marker,
                            cancellationToken)
                        .ConfigureAwait(false))
                {
                    return null;
                }
            }
            foreach (string marker in _markers)
            {
                if (await LanguageServerWorkspaceAccess
                        .MarkerExistsAsync(
                            context.Workspace,
                            current,
                            marker,
                            cancellationToken)
                        .ConfigureAwait(false))
                {
                    return LanguageServerWorkspaceAccess
                        .ToExecutionPath(
                            context.Workspace,
                            current);
                }
            }
            if (current.Equals(workspaceRoot))
            {
                break;
            }
            directory = LanguageServerWorkspaceAccess.Parent(current);
        }

        return _markers.Count == 0
            ? LanguageServerWorkspaceAccess.ToExecutionPath(
                context.Workspace,
                workspaceRoot)
            : null;
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

    private static string Encode(IEnumerable<string> values) =>
        string.Concat(values.Select(static value => $"{value.Length}:{value}"));
}

public sealed class LanguageServerToolResolver : ILanguageServerToolResolver
{
    private static readonly string[] LocalBinDirectories =
    [
        Path.Combine("node_modules", ".bin"),
        Path.Combine(".venv", "bin"),
        Path.Combine(".venv", "Scripts"),
        Path.Combine("venv", "bin"),
        Path.Combine("venv", "Scripts"),
        Path.Combine(".env", "bin"),
        Path.Combine(".env", "Scripts"),
        Path.Combine("vendor", "bundle", "bin"),
        "bin"
    ];

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
            foreach (var localBinDirectory in LocalBinDirectories)
            {
                var binDirectory = Path.Combine(directory, localBinDirectory);
                var candidate = FindExecutableInDirectory(binDirectory, name);
                if (candidate is not null)
                    return candidate;
            }

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

/// <summary>
/// Resolves launch tools through the selected Environment process provider and
/// local tool directories through the session workspace file store.
/// </summary>
public sealed class EnvironmentLanguageServerToolResolver(
    RuntimeProcessExecutionBinding processExecution,
    RuntimeWorkspaceBinding workspace) : ILanguageServerToolResolver
{
    private const int MaximumProbeBytes = 4096;

    /// <inheritdoc />
    public async ValueTask<string?> FindExecutableAsync(
        string name,
        string root,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Path.IsPathRooted(name) ||
            name.Contains('/') ||
            name.Contains('\\'))
        {
            WorkspacePath executable =
                LanguageServerWorkspaceAccess.ResolvePath(
                    workspace,
                    name);
            return await workspace.Files.GetInfoAsync(
                    executable,
                    cancellationToken).ConfigureAwait(false) is
                { Kind: WorkspaceEntryKind.File }
                    ? workspace.Paths.ToExecutionPath(executable).Value
                    : null;
        }

        ProcessInvocationResult result =
            await processExecution.ProcessProvider.RunAsync(
                new ProcessInvocationSpec
                {
                    Target = processExecution.ExecutionTarget,
                    Role = ProcessRole.Sidecar,
                    Command = new ProcessCommandSpec
                    {
                        FileName = "sh",
                        Arguments =
                        [
                            "-lc",
                            "command -v -- \"$1\"",
                            "hpd-language-server-resolve",
                            name,
                        ],
                        WorkingDirectory = root,
                    },
                    Limits = new ProcessLimitSpec(
                        ProcessCount: 4,
                        MemoryBytes: 64L * 1024 * 1024),
                    Io = new ProcessIoSpec
                    {
                        StandardOutput = new ProcessOutputSpec
                        {
                            Capture = true,
                            Stream = false,
                            MaxCapturedBytes = MaximumProbeBytes,
                        },
                        StandardError = new ProcessOutputSpec
                        {
                            Capture = true,
                            Stream = false,
                            MaxCapturedBytes = MaximumProbeBytes,
                        },
                    },
                    Policy = ProcessInvocationPolicy.Default with
                    {
                        Timeout = TimeSpan.FromSeconds(3),
                        StopProcessTree = true,
                    },
                    Isolation = processExecution.Isolation,
                    ObservationRetention =
                        ObservationRetentionPolicy.ResultAndDiagnostics,
                },
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (result.ExitCode != 0 ||
            result.CompletionKind is not (
                ProcessCompletionKind.Completed or
                ProcessCompletionKind.Exited))
        {
            return null;
        }
        string resolved = Encoding.UTF8
            .GetString(result.Output.Stdout.CapturedBytes.Span)
            .Trim()
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return resolved.Length == 0 ||
            !Path.IsPathRooted(resolved)
                ? null
                : resolved;
    }

    /// <inheritdoc />
    public async ValueTask<string?> FindNodeModuleAsync(
        string modulePath,
        string root,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        WorkspacePath start =
            await LanguageServerWorkspaceAccess.ResolveDirectoryAsync(
                workspace,
                root,
                cancellationToken).ConfigureAwait(false);
        while (true)
        {
            WorkspacePath candidate =
                LanguageServerWorkspaceAccess
                    .ResolveRelative(
                        workspace,
                        start,
                        "node_modules/" +
                            modulePath.Replace('\\', '/'));
            if (await workspace.Files.GetInfoAsync(
                    candidate,
                    cancellationToken).ConfigureAwait(false) is not null)
            {
                return workspace.Paths.ToExecutionPath(candidate).Value;
            }
            WorkspacePath? parent =
                LanguageServerWorkspaceAccess.Parent(start);
            if (parent is null)
            {
                return null;
            }
            start = parent.Value;
        }
    }

    /// <inheritdoc />
    public async ValueTask<string?> FindLocalBinAsync(
        string name,
        string root,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        WorkspacePath start =
            await LanguageServerWorkspaceAccess.ResolveDirectoryAsync(
                workspace,
                root,
                cancellationToken).ConfigureAwait(false);
        string[] localBins =
        [
            "node_modules/.bin",
            ".venv/bin",
            ".venv/Scripts",
            "venv/bin",
            "venv/Scripts",
            ".env/bin",
            ".env/Scripts",
            "vendor/bundle/bin",
            "bin",
        ];
        while (true)
        {
            foreach (string bin in localBins)
            {
                WorkspacePath candidate =
                    LanguageServerWorkspaceAccess
                        .ResolveRelative(
                            workspace,
                            start,
                            bin + "/" + name);
                if (await workspace.Files.GetInfoAsync(
                        candidate,
                        cancellationToken).ConfigureAwait(false) is
                    { Kind: WorkspaceEntryKind.File })
                {
                    return workspace.Paths.ToExecutionPath(
                        candidate).Value;
                }
            }
            WorkspacePath? parent =
                LanguageServerWorkspaceAccess.Parent(start);
            if (parent is null)
            {
                return null;
            }
            start = parent.Value;
        }
    }
}

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent;
using HPD.Agent.Harness.Coding;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPDOS.Harneses.Middleware;

/// <summary>
/// Config for <see cref="EnvironmentContextMiddleware"/>.
/// </summary>
public sealed class EnvironmentContextConfig
{
    public string? ShellExecutableOverride { get; init; }

    public string? ShellKindOverride { get; init; }

    public IReadOnlyList<string>? ShellCommandArgumentsPrefixOverride { get; init; }
}

/// <summary>
/// Harness-scoped middleware that tells the model where the coding harness is running.
/// </summary>
public sealed class EnvironmentContextMiddleware : IHarnessMiddleware
{
    private readonly EnvironmentContextConfig _config;

    public EnvironmentContextMiddleware()
        : this(new EnvironmentContextConfig())
    {
    }

    public EnvironmentContextMiddleware(EnvironmentContextConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        var environmentContext = EnvironmentContext.CreateCurrent(_config);
        AgentWorkspace.TryFrom(context.RunConfig, out var workspace, out _);
        var environmentInstructions = environmentContext.SerializeToXml(workspace);
        var state = context.GetMiddlewareState<EnvironmentContextState>();
        if (state?.LastSerializedContext == environmentInstructions)
            return Task.CompletedTask;

        context.Options.Instructions = string.IsNullOrWhiteSpace(context.Options.Instructions)
            ? environmentInstructions
            : $"{context.Options.Instructions}\n\n{environmentInstructions}";

        context.UpdateMiddlewareState<EnvironmentContextState>(s => s with
        {
            LastContext = environmentContext,
            LastSerializedContext = environmentInstructions
        });

        return Task.CompletedTask;
    }

    public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
    {
        if (context.ResultMetadata.TryGet<ReadFileSnapshot>(
                CodingToolMetadataKeys.ReadFileSnapshot,
                out var snapshot))
        {
            RecordReadFileSnapshot(context, snapshot);
            return Task.CompletedTask;
        }

        if (context.ResultMetadata.TryGet<CodingFileMutationSnapshot>(
                CodingToolMetadataKeys.FileMutationSnapshot,
                out var mutation))
        {
            RecordFileMutation(context, mutation);
        }

        return Task.CompletedTask;
    }

    private static void RecordReadFileSnapshot(AfterFunctionContext context, ReadFileSnapshot snapshot)
    {
        context.UpdateMiddlewareState<ReadFileState>(state =>
        {
            var updated = new Dictionary<string, ReadFileSnapshot>(
                state.FilesByPath,
                StringComparer.Ordinal)
            {
                [snapshot.Path] = snapshot
            };

            return state with { FilesByPath = updated };
        });
    }

    private static void RecordFileMutation(AfterFunctionContext context, CodingFileMutationSnapshot mutation)
    {
        context.UpdateMiddlewareState<ReadFileState>(state =>
        {
            var updated = new Dictionary<string, ReadFileSnapshot>(
                state.FilesByPath,
                StringComparer.Ordinal);

            if (mutation.Kind == CodingFileMutationKind.Deleted)
            {
                updated.Remove(mutation.Path);
                return state with { FilesByPath = updated };
            }

            if (mutation.Text == null)
                return state with { FilesByPath = updated };

            var lines = ReadLines(mutation.Text);
            updated[mutation.Path] = new ReadFileSnapshot
            {
                Path = mutation.Path,
                ReadAt = DateTimeOffset.UtcNow,
                LastWriteTimeUtc = mutation.LastWriteTimeUtc ?? DateTimeOffset.UtcNow,
                Length = mutation.ByteLength ?? Encoding.UTF8.GetByteCount(mutation.Text),
                Offset = 1,
                Limit = Math.Max(1, lines.Count),
                StartLine = lines.Count == 0 ? 0 : 1,
                EndLine = lines.Count,
                LinesRead = lines.Count,
                TotalLines = lines.Count,
                Truncated = false,
                Coverage = lines.Count == 0 ? ReadFileCoverage.EmptyFile : ReadFileCoverage.FullFile,
                SourceKind = ReadFileSourceKind.FileSystem,
                ReturnedContentHash = ComputeReturnedContentHash(lines)
            };

            return state with { FilesByPath = updated };
        });
    }

    private static IReadOnlyList<string> ReadLines(string text)
    {
        using var reader = new StringReader(text);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
            lines.Add(line);

        return lines;
    }

    private static string ComputeReturnedContentHash(IReadOnlyList<string> lines)
    {
        var content = string.Join('\n', lines);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// Session-scoped environment middleware state.
/// </summary>
[MiddlewareState(Scope = StateScope.Session)]
public sealed record EnvironmentContextState
{
    public EnvironmentContext? LastContext { get; init; }

    public string? LastSerializedContext { get; init; }
}

public sealed record CodingFileMutationSnapshot
{
    public required string ToolName { get; init; }
    public required string Path { get; init; }
    public required CodingFileMutationKind Kind { get; init; }
    public string? Text { get; init; }
    public long? ByteLength { get; init; }
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
}

public enum CodingFileMutationKind
{
    Created,
    Changed,
    Deleted
}

/// <summary>
/// Minimal environment snapshot for coding harness context.
/// </summary>
public sealed record EnvironmentContext
{
    public string Cwd { get; init; } = Directory.GetCurrentDirectory();

    public string Shell { get; init; } = DetectShellInfo(null).Name;

    public string ShellExecutable { get; init; } = DetectShellInfo(null).Executable;

    public string ShellKind { get; init; } = DetectShellInfo(null).Kind;

    public IReadOnlyList<string> ShellCommandArgumentsPrefix { get; init; } = DetectShellInfo(null).CommandArgumentsPrefix;

    public IReadOnlyList<DetectedShell> AvailableShells { get; init; } = DetectShellInfo(null).AvailableShells;

    public string CurrentDate { get; init; } = DateTime.Now.ToString("yyyy-MM-dd");

    public string Timezone { get; init; } = TimeZoneInfo.Local.Id;

    public string OperatingSystem { get; init; } = DetectOperatingSystem();

    public string OsVersion { get; init; } = Environment.OSVersion.VersionString;

    public bool IsWindows { get; init; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public string DirectorySeparator { get; init; } = Path.DirectorySeparatorChar.ToString();

    public string AltDirectorySeparator { get; init; } = Path.AltDirectorySeparatorChar.ToString();

    public string PathSeparator { get; init; } = Path.PathSeparator.ToString();

    public bool IsGitRepository { get; init; } = DetectGitRepository();

    public string WorkspaceRoot { get; init; } = FindWorkspaceRoot();

    public string TempDirectory { get; init; } = Path.GetTempPath();

    public static EnvironmentContext CreateCurrent(EnvironmentContextConfig? config = null)
    {
        var cwd = Directory.GetCurrentDirectory();
        var gitRoot = FindGitRoot();
        var shellInfo = DetectShellInfo(config);

        return new EnvironmentContext
        {
            Cwd = cwd,
            Shell = shellInfo.Name,
            ShellExecutable = shellInfo.Executable,
            ShellKind = shellInfo.Kind,
            ShellCommandArgumentsPrefix = shellInfo.CommandArgumentsPrefix,
            AvailableShells = shellInfo.AvailableShells,
            CurrentDate = DateTimeOffset.Now.ToString("yyyy-MM-dd"),
            Timezone = TimeZoneInfo.Local.Id,
            OperatingSystem = DetectOperatingSystem(),
            OsVersion = Environment.OSVersion.VersionString,
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            DirectorySeparator = Path.DirectorySeparatorChar.ToString(),
            AltDirectorySeparator = Path.AltDirectorySeparatorChar.ToString(),
            PathSeparator = Path.PathSeparator.ToString(),
            IsGitRepository = gitRoot != null,
            WorkspaceRoot = gitRoot ?? cwd,
            TempDirectory = Path.GetTempPath()
        };
    }

    public string SerializeToXml(AgentWorkspace? workspace = null)
    {
        var builder = new StringBuilder();

        builder.AppendLine("  <environment_context>");
        builder.AppendLine($"    <cwd>{EscapeXml(Cwd)}</cwd>");
        builder.AppendLine($"    <shell>{EscapeXml(Shell)}</shell>");
        builder.AppendLine($"    <shell_executable>{EscapeXml(ShellExecutable)}</shell_executable>");
        builder.AppendLine($"    <shell_kind>{EscapeXml(ShellKind)}</shell_kind>");
        builder.AppendLine("    <shell_command_arguments>");
        foreach (var argument in ShellCommandArgumentsPrefix)
            builder.AppendLine($"      <arg>{EscapeXml(argument)}</arg>");
        builder.AppendLine("    </shell_command_arguments>");
        builder.AppendLine($"    <current_date>{EscapeXml(CurrentDate)}</current_date>");
        builder.AppendLine($"    <timezone>{EscapeXml(Timezone)}</timezone>");
        builder.AppendLine($"    <operating_system>{EscapeXml(OperatingSystem)}</operating_system>");
        builder.AppendLine($"    <os_version>{EscapeXml(OsVersion)}</os_version>");
        builder.AppendLine($"    <is_windows>{IsWindows.ToString().ToLowerInvariant()}</is_windows>");
        builder.AppendLine($"    <directory_separator>{EscapeXml(DirectorySeparator)}</directory_separator>");
        builder.AppendLine($"    <alt_directory_separator>{EscapeXml(AltDirectorySeparator)}</alt_directory_separator>");
        builder.AppendLine($"    <path_separator>{EscapeXml(PathSeparator)}</path_separator>");
        builder.AppendLine($"    <is_git_repository>{IsGitRepository.ToString().ToLowerInvariant()}</is_git_repository>");
        builder.AppendLine($"    <workspace_root>{EscapeXml(WorkspaceRoot)}</workspace_root>");
        if (workspace is not null)
        {
            builder.AppendLine("    <selected_workspace>");
            builder.AppendLine($"      <default_root_id>{EscapeXml(workspace.DefaultRootId)}</default_root_id>");
            builder.AppendLine($"      <default_root_path>{EscapeXml(workspace.DefaultRootPath)}</default_root_path>");
            builder.AppendLine("      <roots>");
            foreach (var root in workspace.Roots)
            {
                builder.AppendLine(
                    $"        <root id=\"{EscapeXml(root.Id)}\" label=\"{EscapeXml(root.Label ?? root.Id)}\" path=\"{EscapeXml(root.Path)}\" />");
            }
            builder.AppendLine("      </roots>");
            builder.AppendLine("    </selected_workspace>");
        }
        builder.AppendLine($"    <temp_directory>{EscapeXml(TempDirectory)}</temp_directory>");
        builder.AppendLine("    <available_shells>");
        foreach (var shell in AvailableShells)
        {
            builder.AppendLine(
                $"      <shell name=\"{EscapeXml(shell.Name)}\" executable=\"{EscapeXml(shell.Executable)}\" kind=\"{EscapeXml(shell.Kind)}\" source=\"{EscapeXml(shell.Source)}\" available=\"{shell.Available.ToString().ToLowerInvariant()}\" selected=\"{shell.Selected.ToString().ToLowerInvariant()}\" />");
        }
        builder.AppendLine("    </available_shells>");
        builder.Append("  </environment_context>");

        return builder.ToString();
    }

    private static ShellInfo DetectShellInfo(EnvironmentContextConfig? config)
    {
        var candidates = new List<DetectedShell>();

        if (!string.IsNullOrWhiteSpace(config?.ShellExecutableOverride))
        {
            var executable = config.ShellExecutableOverride!;
            var kind = string.IsNullOrWhiteSpace(config.ShellKindOverride)
                ? InferShellKind(executable)
                : config.ShellKindOverride!;
            var prefix = config.ShellCommandArgumentsPrefixOverride?.ToArray()
                ?? GetDefaultShellArgumentsPrefix(kind);

            var selected = new DetectedShell(
                Name: Path.GetFileNameWithoutExtension(executable),
                Executable: executable,
                Kind: kind,
                Source: "config",
                Available: true,
                Selected: true);

            candidates.Add(selected);
            return new ShellInfo(
                selected.Name,
                selected.Executable,
                selected.Kind,
                prefix,
                candidates);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return DetectWindowsShellInfo(candidates);

        return DetectPosixShellInfo(candidates);
    }

    private static ShellInfo DetectPosixShellInfo(List<DetectedShell> candidates)
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        AddPosixCandidate(candidates, shell, "SHELL");
        AddPosixCandidate(candidates, "/bin/zsh", "well_known");
        AddPosixCandidate(candidates, "/bin/bash", "well_known");
        AddPosixCandidate(candidates, "/bin/sh", "well_known");

        var selected = candidates.FirstOrDefault(candidate => candidate.Available && candidate.Kind == "posix")
            ?? candidates.FirstOrDefault(candidate => candidate.Available)
            ?? new DetectedShell("sh", "/bin/sh", "posix", "fallback", true, false);

        return BuildShellInfo(selected, candidates);
    }

    private static ShellInfo DetectWindowsShellInfo(List<DetectedShell> candidates)
    {
        AddWindowsPathCandidate(candidates, "pwsh", "PATH");
        AddWindowsPathCandidate(candidates, "powershell.exe", "PATH");

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            AddWindowsFileCandidate(
                candidates,
                Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                "well_known");
        }

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        AddWindowsFileCandidate(candidates, comSpec, "ComSpec");
        AddWindowsPathCandidate(candidates, "cmd.exe", "PATH");

        var selected = candidates.FirstOrDefault(candidate => candidate.Available && candidate.Kind == "powershell")
            ?? candidates.FirstOrDefault(candidate => candidate.Available && candidate.Kind == "cmd")
            ?? candidates.FirstOrDefault(candidate => candidate.Available)
            ?? new DetectedShell("cmd", "cmd.exe", "cmd", "fallback", true, false);

        return BuildShellInfo(selected, candidates);
    }

    private static ShellInfo BuildShellInfo(DetectedShell selected, List<DetectedShell> candidates)
    {
        var selectedShell = selected with { Selected = true };
        var normalizedCandidates = candidates
            .Select(candidate => candidate.Executable == selected.Executable
                ? candidate with { Selected = true }
                : candidate with { Selected = false })
            .ToList();

        if (!normalizedCandidates.Any(candidate =>
                string.Equals(candidate.Executable, selectedShell.Executable, StringComparison.OrdinalIgnoreCase)))
        {
            normalizedCandidates.Add(selectedShell);
        }

        return new ShellInfo(
            selectedShell.Name,
            selectedShell.Executable,
            selectedShell.Kind,
            GetDefaultShellArgumentsPrefix(selectedShell.Kind),
            normalizedCandidates);
    }

    private static void AddPosixCandidate(List<DetectedShell> candidates, string? executable, string source)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return;

        var fullPath = Path.IsPathRooted(executable)
            ? executable
            : FindExecutableOnPath(executable);
        if (string.IsNullOrWhiteSpace(fullPath))
            return;

        AddCandidate(candidates, fullPath, source, IsExecutableFile(fullPath));
    }

    private static void AddWindowsPathCandidate(List<DetectedShell> candidates, string executable, string source)
    {
        var fullPath = FindExecutableOnPath(executable);
        if (!string.IsNullOrWhiteSpace(fullPath))
            AddCandidate(candidates, fullPath, source, true);
    }

    private static void AddWindowsFileCandidate(List<DetectedShell> candidates, string? executable, string source)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return;

        AddCandidate(candidates, executable, source, File.Exists(executable));
    }

    private static void AddCandidate(List<DetectedShell> candidates, string executable, string source, bool available)
    {
        if (candidates.Any(candidate => string.Equals(candidate.Executable, executable, StringComparison.OrdinalIgnoreCase)))
            return;

        candidates.Add(new DetectedShell(
            Name: Path.GetFileNameWithoutExtension(executable),
            Executable: executable,
            Kind: InferShellKind(executable),
            Source: source,
            Available: available,
            Selected: false));
    }

    private static string InferShellKind(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable);

        if (name.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("powershell", StringComparison.OrdinalIgnoreCase))
            return "powershell";

        if (name.Equals("cmd", StringComparison.OrdinalIgnoreCase))
            return "cmd";

        if (name.Equals("sh", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("zsh", StringComparison.OrdinalIgnoreCase))
            return "posix";

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "posix";
    }

    private static IReadOnlyList<string> GetDefaultShellArgumentsPrefix(string shellKind)
        => shellKind switch
        {
            "powershell" => ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command"],
            "cmd" => ["/C"],
            _ => ["-lc"]
        };

    private static string? FindExecutableOnPath(string executable)
    {
        if (Path.IsPathRooted(executable))
            return File.Exists(executable) ? executable : null;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var names = GetExecutableCandidateNames(executable);
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate) && (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IsExecutableFile(candidate)))
                    return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetExecutableCandidateNames(string executable)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Path.HasExtension(executable))
            return [executable];

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(pathExt)
            ? [".exe", ".cmd", ".bat", ".ps1"]
            : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return extensions
            .Select(extension => executable + extension)
            .Prepend(executable)
            .ToArray();
    }

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & UnixFileMode.UserExecute) != 0 ||
                   (mode & UnixFileMode.GroupExecute) != 0 ||
                   (mode & UnixFileMode.OtherExecute) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool DetectGitRepository()
    {
        return FindGitRoot() != null;
    }

    private static string FindWorkspaceRoot()
    {
        return FindGitRoot() ?? Directory.GetCurrentDirectory();
    }

    private static string? FindGitRoot()
    {
        try
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                var gitPath = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string DetectOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "darwin";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "windows";

        return "unknown";
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

public sealed record DetectedShell(
    string Name,
    string Executable,
    string Kind,
    string Source,
    bool Available,
    bool Selected);

internal sealed record ShellInfo(
    string Name,
    string Executable,
    string Kind,
    IReadOnlyList<string> CommandArgumentsPrefix,
    IReadOnlyList<DetectedShell> AvailableShells);

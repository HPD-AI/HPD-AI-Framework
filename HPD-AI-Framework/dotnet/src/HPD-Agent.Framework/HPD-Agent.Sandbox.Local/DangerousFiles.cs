using System.Diagnostics;

namespace HPD.Sandbox.Local;

/// <summary>
/// Files that are always blocked from sandbox access.
/// </summary>
internal static class DangerousFiles
{
    /// <summary>
    /// Environment files that may contain secrets.
    /// </summary>
    public static readonly string[] EnvironmentFiles =
    [
        ".env",
        ".env.local",
        ".env.development",
        ".env.development.local",
        ".env.test",
        ".env.test.local",
        ".env.production",
        ".env.production.local",
        ".env.staging",
        ".envrc",
    ];

    /// <summary>
    /// Directories that contain sensitive data or executable code.
    /// </summary>
    public static readonly string[] DangerousDirectories =
    [
        ".claude",      // Claude Code settings
        "node_modules/.cache",  // May contain cached credentials
    ];

    /// <summary>
    /// Git paths that could be used for code injection.
    /// </summary>
    public static readonly string[] GitPaths =
    [
        ".git/hooks",   // Pre-commit hooks could execute code
        ".git/config",  // Could contain credentials or malicious remotes
    ];

    /// <summary>
    /// Gets all paths to block (relative to working directory).
    /// </summary>
    public static IEnumerable<string> GetAllBlockedPaths(string workingDirectory)
    {
        // Files in current directory
        foreach (var file in EnvironmentFiles)
            yield return Path.Combine(workingDirectory, file);

        // Directories in current directory
        foreach (var dir in DangerousDirectories)
            yield return Path.Combine(workingDirectory, dir);

        // Git paths
        foreach (var gitPath in GitPaths)
            yield return Path.Combine(workingDirectory, gitPath);

        // Also block in subdirectories (glob patterns for macOS, ripgrep for Linux)
        foreach (var file in EnvironmentFiles)
            yield return $"**/{file}";

        foreach (var dir in DangerousDirectories)
            yield return $"**/{dir}/**";

        foreach (var gitPath in GitPaths)
            yield return $"**/{gitPath}";
    }

    /// <summary>
    /// Scans for dangerous files in the working directory (Linux only).
    /// </summary>
    internal static async Task<string[]> ScanForDangerousFilesAsync(
        string workingDirectory,
        int maxDepth = 3,
        CancellationToken cancellationToken = default)
    {
        var patterns = EnvironmentFiles
            .Concat(DangerousDirectories.Select(d => $"**/{d}/**"))
            .Concat(GitPaths.Select(p => $"**/{p}"));

        var args = new List<string>
        {
            "--files",
            "--hidden",
            "--max-depth", maxDepth.ToString(),
            "-g", "!**/node_modules/**",  // Skip node_modules for performance
        };

        foreach (var pattern in patterns)
            args.AddRange(["--iglob", pattern]);

        var result = await RipgrepAsync(args, workingDirectory, cancellationToken);
        return result.ToArray();
    }

    private static async Task<IEnumerable<string>> RipgrepAsync(
        List<string> args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "rg",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };

            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return [];
        }
    }
}

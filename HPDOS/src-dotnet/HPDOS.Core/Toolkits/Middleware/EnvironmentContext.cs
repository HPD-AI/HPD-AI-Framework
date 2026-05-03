using System.Runtime.InteropServices;
using System.Text;

namespace HPDOS.Harneses.Middleware;

/// <summary>
/// Captures the execution environment context for the agent.
/// </summary>
public class EnvironmentContext
{
    public string Cwd { get; init; } = Directory.GetCurrentDirectory();
    public string Shell { get; init; } = DetectShell();
    public string Platform { get; init; } = DetectPlatform();
    public string OsVersion { get; init; } = Environment.OSVersion.ToString();
    public IReadOnlyList<string>? WritableRoots { get; init; }
    public string? NetworkAccess { get; init; }
    public bool IsGitRepo { get; init; } = DetectGitRepo();
    public string TodaysDate { get; init; } = DateTime.Now.ToString("yyyy-MM-dd");
    public string? DirectoryListing { get; init; }

    public static EnvironmentContext CreateCurrent(IReadOnlyList<string>? writableRoots = null, bool includeDirectoryListing = true)
    {
        var cwd = Directory.GetCurrentDirectory();
        return new EnvironmentContext
        {
            Cwd = cwd,
            Shell = DetectShell(),
            Platform = DetectPlatform(),
            OsVersion = Environment.OSVersion.ToString(),
            WritableRoots = writableRoots,
            IsGitRepo = DetectGitRepo(),
            TodaysDate = DateTime.Now.ToString("yyyy-MM-dd"),
            DirectoryListing = includeDirectoryListing ? GenerateDirectoryListing(cwd) : null
        };
    }

    public string SerializeToXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<environment_context>");
        sb.AppendLine($"  <cwd>{EscapeXml(Cwd)}</cwd>");
        sb.AppendLine($"  <shell>{EscapeXml(Shell)}</shell>");
        sb.AppendLine($"  <platform>{EscapeXml(Platform)}</platform>");
        sb.AppendLine($"  <os_version>{EscapeXml(OsVersion)}</os_version>");
        sb.AppendLine($"  <is_git_repo>{IsGitRepo.ToString().ToLowerInvariant()}</is_git_repo>");
        sb.AppendLine($"  <todays_date>{TodaysDate}</todays_date>");

        if (WritableRoots != null && WritableRoots.Count > 0)
        {
            sb.AppendLine("  <writable_roots>");
            foreach (var root in WritableRoots)
                sb.AppendLine($"    <root>{EscapeXml(root)}</root>");
            sb.AppendLine("  </writable_roots>");
        }

        if (!string.IsNullOrEmpty(NetworkAccess))
            sb.AppendLine($"  <network_access>{EscapeXml(NetworkAccess)}</network_access>");

        if (!string.IsNullOrEmpty(DirectoryListing))
        {
            sb.AppendLine("  <directory_listing>");
            sb.AppendLine($"    # Current Directory ({Cwd}) Files");
            sb.AppendLine();
            sb.AppendLine(DirectoryListing);
            sb.AppendLine("  </directory_listing>");
        }

        sb.AppendLine("</environment_context>");
        return sb.ToString();
    }

    public static EnvironmentContext? Diff(EnvironmentContext before, EnvironmentContext after)
    {
        if (before.Cwd == after.Cwd)
            return null;

        return new EnvironmentContext
        {
            Cwd = after.Cwd,
            Shell = after.Shell,
            Platform = after.Platform,
            OsVersion = after.OsVersion,
            WritableRoots = after.WritableRoots,
            IsGitRepo = after.IsGitRepo,
            TodaysDate = after.TodaysDate
        };
    }

    private static string DetectShell()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell))
        {
            if (shell.Contains("zsh")) return "zsh";
            if (shell.Contains("bash")) return "bash";
            if (shell.Contains("fish")) return "fish";
            return Path.GetFileName(shell);
        }

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (!string.IsNullOrEmpty(comSpec))
        {
            if (comSpec.Contains("powershell", StringComparison.OrdinalIgnoreCase)) return "pwsh";
            if (comSpec.Contains("cmd", StringComparison.OrdinalIgnoreCase)) return "cmd";
            return Path.GetFileName(comSpec);
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PSModulePath")))
            return "pwsh";

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "bash";
    }

    private static string DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "darwin";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        return "unknown";
    }

    private static bool DetectGitRepo()
    {
        try
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return true;
                dir = dir.Parent;
            }
            return false;
        }
        catch { return false; }
    }

    private static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string GenerateDirectoryListing(string directory, int maxItems = 200)
    {
        try
        {
            var sb = new StringBuilder();
            var items = new List<string>();
            var gitignorePatterns = LoadGitignorePatterns(directory);
            var dirInfo = new DirectoryInfo(directory);

            foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name))
            {
                if (ShouldIgnore(dir.Name, gitignorePatterns, isDirectory: true)) continue;
                items.Add(dir.Name + "/");
                if (items.Count >= maxItems) break;
            }

            if (items.Count < maxItems)
            {
                foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name))
                {
                    if (ShouldIgnore(file.Name, gitignorePatterns, isDirectory: false)) continue;
                    items.Add(file.Name);
                    if (items.Count >= maxItems) break;
                }
            }

            foreach (var item in items)
                sb.AppendLine($"    {item}");

            if (items.Count >= maxItems)
            {
                sb.AppendLine();
                sb.AppendLine("    (File list truncated. Use file tools to explore further.)");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"    Error listing directory: {ex.Message}";
        }
    }

    private static HashSet<string> LoadGitignorePatterns(string directory)
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", ".vs", "bin", "obj", ".vscode", ".idea",
            "*.swp", "*.swo", ".DS_Store"
        };

        try
        {
            var gitignorePath = Path.Combine(directory, ".gitignore");
            if (File.Exists(gitignorePath))
            {
                foreach (var line in File.ReadAllLines(gitignorePath))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                        patterns.Add(trimmed.TrimStart('/'));
                }
            }
        }
        catch { }

        return patterns;
    }

    private static bool ShouldIgnore(string name, HashSet<string> patterns, bool isDirectory)
    {
        if (patterns.Contains(name)) return true;

        foreach (var pattern in patterns)
        {
            if (pattern.Contains("*"))
            {
                var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;
            }
        }

        return false;
    }
}

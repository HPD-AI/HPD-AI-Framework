using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using DiffPlex.DiffBuilder.Model;
using HPD.Agent;
using HPDOS.Toolkits.Middleware;
using MAB.DotIgnore;
using Ude;

/// <summary>
/// CodingToolkit - Comprehensive coding assistant with file operations, search, execution, and analysis.
/// Features: Line-based reading, smart diff-based editing, glob patterns, .gitignore support, grep search, shell execution.
/// 
/// Organized as partial classes - each major function group is in its own file:
/// - ReadFileTool.cs: ReadFile with timeout-aware streaming
/// - ReadManyFilesTool.cs: Batch file reading from glob patterns
/// - EditFileTool.cs: Smart text replacement (exact, flexible, fuzzy)
/// - WriteFileTool.cs: File writing with diff preview
/// - ListDirectoryTool.cs: Directory listing with metadata
/// - GlobSearchTool.cs: File search using glob patterns
/// - GrepTool.cs: Content search with regex
/// - DiffFilesTool.cs: File comparison
/// - FileInfoTool.cs: File metadata retrieval
/// - ExecuteCommandTool.cs: Cross-platform shell execution
/// - CodingToolkit.cs (this file): Shared helpers and infrastructure
/// </summary>
[Collapse(
    "Contains tools for coding operations: file operations, code search, shell execution, and code analysis.",
    Middlewares = [typeof(EnvironmentContextMiddleware)])]
public partial class CodingToolkit
{
    private readonly IgnoreList? _gitIgnoreList = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), ".gitignore"))
        ? new IgnoreList(Path.Combine(Directory.GetCurrentDirectory(), ".gitignore"))
        : null;

    protected static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bin", ".obj", ".o",
        ".zip", ".tar", ".gz", ".rar", ".7z",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp",
        ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".woff", ".woff2", ".ttf", ".eot",
        ".pyc", ".class", ".pdb"
    };

    protected static readonly HashSet<string> DefaultIgnoreDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea",
        "__pycache__", "venv", ".venv", "dist", "build", "target",
        ".next", ".nuxt", "coverage", ".cache"
    };

    // ═══════════════════════════════════════════════════════════════════
    // SHARED HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════

    protected static int CountOccurrences(string text, string substring)
    {
        if (string.IsNullOrEmpty(substring))
            return 0;

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    protected static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        int pos = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (pos < 0)
            return text;

        return text[..pos] + newValue + text[(pos + oldValue.Length)..];
    }

    protected static string GetIndentation(string line)
    {
        var match = Regex.Match(line, @"^(\s*)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    protected static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    protected static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
        if (age.TotalDays < 7) return $"{(int)age.TotalDays}d ago";
        if (age.TotalDays < 30) return $"{(int)(age.TotalDays / 7)}w ago";
        return $"{(int)(age.TotalDays / 30)}mo ago";
    }

    protected static bool IsBinaryFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (BinaryExtensions.Contains(ext))
            return true;

        try
        {
            using var fs = File.OpenRead(filePath);
            var buffer = new byte[8192];
            var bytesRead = fs.Read(buffer, 0, buffer.Length);

            if (bytesRead == 0) return false;

            var nonPrintable = 0;
            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0) return true; // null byte — strong binary indicator
                if (buffer[i] < 9 || (buffer[i] > 13 && buffer[i] < 32))
                    nonPrintable++;
            }
            return (double)nonPrintable / bytesRead > 0.30;
        }
        catch
        {
            return true;
        }
    }

    protected static Encoding? DetectEncoding(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var detector = new CharsetDetector();
            detector.Feed(fs);
            detector.DataEnd();

            if (detector.Charset != null)
            {
                return Encoding.GetEncoding(detector.Charset);
            }
        }
        catch
        {
            // Fall back to UTF8
        }
        return null;
    }

    protected static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "text/x-csharp",
            ".js" => "text/javascript",
            ".ts" => "text/typescript",
            ".tsx" => "text/typescript-jsx",
            ".jsx" => "text/javascript-jsx",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" => "text/html",
            ".css" => "text/css",
            ".md" => "text/markdown",
            ".py" => "text/x-python",
            ".java" => "text/x-java",
            ".go" => "text/x-go",
            ".rs" => "text/x-rust",
            ".cpp" or ".cc" or ".cxx" => "text/x-c++",
            ".c" or ".h" => "text/x-c",
            ".rb" => "text/x-ruby",
            ".php" => "text/x-php",
            ".swift" => "text/x-swift",
            ".kt" => "text/x-kotlin",
            ".scala" => "text/x-scala",
            ".sql" => "text/x-sql",
            ".sh" or ".bash" => "text/x-shellscript",
            ".ps1" => "text/x-powershell",
            ".yaml" or ".yml" => "text/yaml",
            ".toml" => "text/toml",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".log" => "text/plain",
            ".env" => "text/plain",
            ".gitignore" => "text/plain",
            ".dockerfile" or "" when extension == "Dockerfile" => "text/x-dockerfile",
            _ => "application/octet-stream"
        };
    }

    protected IEnumerable<string> FilterIgnoredFiles(IEnumerable<string> files, string rootPath)
    {
        if (_gitIgnoreList == null)
        {
            foreach (var file in files)
                yield return file;
            yield break;
        }

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(rootPath, file);

            if (!_gitIgnoreList.IsIgnored(relativePath, pathIsDirectory: false))
                yield return file;
        }
    }

    protected static string GenerateDiffDisplay(DiffPlex.DiffBuilder.Model.DiffPaneModel diff, int maxLines = 30)
    {
        var sb = new StringBuilder();
        const int contextLines = 3;

        var changedLineIndices = diff.Lines
            .Select((line, index) => (line, index))
            .Where(x => x.line.Type != DiffPlex.DiffBuilder.Model.ChangeType.Unchanged)
            .Select(x => x.index)
            .ToList();

        if (changedLineIndices.Count == 0)
            return "(no changes)\n";

        // Find ranges to display
        var ranges = new List<(int start, int end)>();
        foreach (var idx in changedLineIndices)
        {
            var start = Math.Max(0, idx - contextLines);
            var end = Math.Min(diff.Lines.Count - 1, idx + contextLines);

            if (ranges.Count > 0 && start <= ranges[^1].end + 1)
            {
                ranges[^1] = (ranges[^1].start, end);
            }
            else
            {
                ranges.Add((start, end));
            }
        }

        var displayedLines = 0;
        foreach (var (start, end) in ranges)
        {
            if (displayedLines >= maxLines)
            {
                sb.AppendLine("... (more changes not shown)");
                break;
            }

            for (int i = start; i <= end && displayedLines < maxLines; i++)
            {
                var line = diff.Lines[i];
                var prefix = line.Type switch
                {
                    DiffPlex.DiffBuilder.Model.ChangeType.Inserted => "+ ",
                    DiffPlex.DiffBuilder.Model.ChangeType.Deleted => "- ",
                    DiffPlex.DiffBuilder.Model.ChangeType.Modified => "! ",
                    _ => "  "
                };

                sb.AppendLine($"{prefix}{line.Text}");
                displayedLines++;
            }

            if (ranges.Count > 1 && (start, end) != ranges[^1])
            {
                sb.AppendLine("...");
            }
        }

        return sb.ToString();
    }

    // Shell command helpers
    protected static (string shell, string args) GetShellExecutable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("cmd.exe", "/c");

        return (Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash", "-c");
    }

    protected static string FormatCommandResult(string command, string workingDir, int exitCode, string output, string error, long duration, bool timedOut)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Command: {command}");
        sb.AppendLine($"Working Directory: {workingDir}");
        sb.AppendLine($"Duration: {duration}ms");
        sb.AppendLine($"Exit Code: {exitCode}");
        sb.AppendLine("---");

        if (!string.IsNullOrWhiteSpace(output))
        {
            sb.AppendLine("OUTPUT:");
            sb.AppendLine(TruncateOutput(output, maxLines: 100));
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            sb.AppendLine();
            sb.AppendLine("ERROR:");
            sb.AppendLine(TruncateOutput(error, maxLines: 50));
        }

        sb.AppendLine("---");
        if (timedOut)
        {
            sb.AppendLine("⏱ TIMED OUT");
        }
        else if (exitCode == 0)
        {
            sb.AppendLine("✓ SUCCESS");
        }
        else
        {
            sb.AppendLine($"✗ FAILED (Exit Code: {exitCode})");
        }

        return sb.ToString();
    }

    protected static string TruncateOutput(string text, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = text.Split('\n');
        if (lines.Length <= maxLines)
            return text;

        var truncated = string.Join('\n', lines.Take(maxLines));
        return $"{truncated}\n... ({lines.Length - maxLines} more lines truncated)";
    }
}

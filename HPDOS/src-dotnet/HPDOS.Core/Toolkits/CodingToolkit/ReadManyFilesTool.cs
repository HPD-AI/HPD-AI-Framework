using System.Text;
using HPD.Agent;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

using Matcher = Microsoft.Extensions.FileSystemGlobbing.Matcher;

/// <summary>
/// ReadManyFiles implementation for CodingToolkit (partial class).
/// Reads multiple files matching glob patterns and concatenates their contents.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("Read and concatenate content from multiple files matching glob patterns. Useful for getting an overview of a codebase or analyzing multiple related files.")]
    public async Task<string> ReadManyFiles(
        [AIDescription("Glob patterns to match files (e.g., '**/*.cs', '*.md', 'src/**/*.json')")] string[] patterns,
        [AIDescription("Root directory to search from.")] string rootPath,
        [AIDescription("Optional: Glob patterns to exclude (e.g., '**/bin/**', '**/obj/**')")] string[]? exclude = null)
    {
        if (patterns == null || patterns.Length == 0)
            return "Error: At least one pattern must be provided.";

        if (!Directory.Exists(rootPath))
            return $"Error: Directory not found: {rootPath}";

        try
        {
            var matcher = new Matcher();

            foreach (var pattern in patterns.Where(p => !string.IsNullOrWhiteSpace(p)))
                matcher.AddInclude(pattern);

            // Add user excludes
            if (exclude != null)
            {
                foreach (var pattern in exclude.Where(p => !string.IsNullOrWhiteSpace(p)))
                    matcher.AddExclude(pattern);
            }

            // Add default excludes
            foreach (var dir in DefaultIgnoreDirs)
                matcher.AddExclude($"**/{dir}/**");

            var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(rootPath)));

            var matchedFiles = matchResult.Files
                .Select(f => Path.Combine(rootPath, f.Path))
                .ToList();

            // Apply gitignore filtering
            matchedFiles = FilterIgnoredFiles(matchedFiles, rootPath).ToList();

            if (matchedFiles.Count == 0)
                return $"No files found matching patterns: {string.Join(", ", patterns)}";

            const int maxFiles = 50;
            var filesToRead = matchedFiles.Take(maxFiles).ToList();
            var skippedFiles = new List<string>();
            var contentParts = new List<string>();

            // Read files in parallel
            var readTasks = filesToRead.Select(async path =>
            {
                try
                {
                    if (IsBinaryFile(path))
                        return (Path: path, Content: (string?)null, Error: (string?)"binary file");

                    var encoding = DetectEncoding(path) ?? Encoding.UTF8;
                    var fileContent = await File.ReadAllTextAsync(path, encoding);
                    return (Path: path, Content: (string?)fileContent, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Path: path, Content: (string?)null, Error: (string?)ex.Message);
                }
            });

            var results = await Task.WhenAll(readTasks);

            foreach (var (filePath, content, error) in results)
            {
                var relativePath = Path.GetRelativePath(rootPath, filePath);

                if (error != null)
                {
                    skippedFiles.Add($"{relativePath} ({error})");
                    continue;
                }

                if (content != null)
                {
                    contentParts.Add($"--- {relativePath} ---\n\n{content}\n");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Read {contentParts.Count} file(s) matching patterns: {string.Join(", ", patterns)} ===");
            sb.AppendLine();

            if (skippedFiles.Count > 0)
            {
                sb.AppendLine($"Skipped {skippedFiles.Count} file(s):");
                foreach (var skipped in skippedFiles.Take(10))
                    sb.AppendLine($"  - {skipped}");
                if (skippedFiles.Count > 10)
                    sb.AppendLine($"  ... and {skippedFiles.Count - 10} more");
                sb.AppendLine();
            }

            if (matchedFiles.Count > maxFiles)
            {
                sb.AppendLine($"Note: Showing first {maxFiles} of {matchedFiles.Count} matching files.");
                sb.AppendLine();
            }

            foreach (var part in contentParts)
                sb.AppendLine(part);

            sb.AppendLine("--- End of content ---");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading multiple files: {ex.Message}";
        }
    }
}

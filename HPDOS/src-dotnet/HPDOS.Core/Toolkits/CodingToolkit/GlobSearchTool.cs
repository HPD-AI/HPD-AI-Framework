using System.Text;
using HPD.Agent;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

using Matcher = Microsoft.Extensions.FileSystemGlobbing.Matcher;

/// <summary>
/// GlobSearch implementation for CodingToolkit (partial class).
/// Searches for files using glob patterns with .gitignore support.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("Search for files using glob patterns (e.g., '**/*.cs', 'src/**/*.ts'). Respects .gitignore patterns.")]
    public string GlobSearch(
        [AIDescription("Root directory to search from.")] string rootPath,
        [AIDescription("Glob pattern (e.g., '**/*.cs', 'src/**/*.json').")] string pattern,
        [AIDescription("Maximum results to return. Default: 100")] int maxResults = 100)
    {
        if (!Directory.Exists(rootPath))
            return $"Error: Directory not found: {rootPath}";

        try
        {
            var matcher = new Matcher();
            matcher.AddInclude(pattern);

            // Exclude common directories
            foreach (var dir in DefaultIgnoreDirs)
                matcher.AddExclude($"**/{dir}/**");

            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(rootPath)));

            if (!result.HasMatches)
                return $"No files found matching '{pattern}'";

            // Get file info and apply ignore filtering
            var matchedFiles = result.Files
                .Select(f => Path.Combine(rootPath, f.Path))
                .ToList();

            matchedFiles = FilterIgnoredFiles(matchedFiles, rootPath).ToList();

            // Sort by most recently modified
            var sortedFiles = matchedFiles
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Found {sortedFiles.Count} file(s) matching '{pattern}':");
            sb.AppendLine("---");

            var displayCount = Math.Min(sortedFiles.Count, maxResults);
            for (int i = 0; i < displayCount; i++)
            {
                var file = sortedFiles[i];
                var relativePath = Path.GetRelativePath(rootPath, file.FullName);
                var size = FormatFileSize(file.Length);
                var age = FormatAge(DateTime.Now - file.LastWriteTime);

                sb.AppendLine($"{i + 1}. {relativePath} ({size}, modified {age})");
            }

            if (sortedFiles.Count > maxResults)
            {
                sb.AppendLine($"... and {sortedFiles.Count - maxResults} more files");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching files: {ex.Message}";
        }
    }
}

using System.Text;
using System.Text.RegularExpressions;
using HPD.Agent;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

using Matcher = Microsoft.Extensions.FileSystemGlobbing.Matcher;

/// <summary>
/// Grep implementation for CodingToolkit (partial class).
/// Searches file contents using regex patterns with context.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("Search file contents using regex pattern. Returns matching lines with context.")]
    public string Grep(
        [AIDescription("Root directory to search in.")] string rootPath,
        [AIDescription("Regex pattern to search for.")] string pattern,
        [AIDescription("File glob pattern to filter (e.g., '*.cs'). Default: all files")] string includeFiles = "",
        [AIDescription("Case-insensitive search. Default: true")] bool ignoreCase = true,
        [AIDescription("Maximum results. Default: 50")] int maxResults = 50)
    {
        if (!Directory.Exists(rootPath))
            return $"Error: Directory not found: {rootPath}";

        try
        {
            var regexOptions = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var regex = new Regex(pattern, regexOptions | RegexOptions.Compiled);

            // Get files to search
            var matcher = new Matcher();
            matcher.AddInclude(string.IsNullOrWhiteSpace(includeFiles) ? "**/*" : includeFiles);
            foreach (var dir in DefaultIgnoreDirs)
                matcher.AddExclude($"**/{dir}/**");
            foreach (var ext in BinaryExtensions)
                matcher.AddExclude($"**/*{ext}");

            var filesToSearch = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(rootPath)));

            var searchPaths = filesToSearch.Files.Select(f => Path.Combine(rootPath, f.Path)).ToList();
            searchPaths = FilterIgnoredFiles(searchPaths, rootPath).ToList();

            var results = new List<(string File, int Line, string Content)>();

            foreach (var fullPath in searchPaths)
            {
                if (results.Count >= maxResults) break;

                try
                {
                    var lines = File.ReadAllLines(fullPath);
                    for (var i = 0; i < lines.Length && results.Count < maxResults; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            var relativePath = Path.GetRelativePath(rootPath, fullPath);
                            results.Add((relativePath, i + 1, lines[i].Trim()));
                        }
                    }
                }
                catch
                {
                    // Skip unreadable files
                }
            }

            if (results.Count == 0)
                return $"No matches found for pattern '{pattern}'";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} match(es) for '{pattern}':");
            sb.AppendLine("---");

            // Group by file
            var byFile = results.GroupBy(r => r.File);
            foreach (var group in byFile)
            {
                sb.AppendLine($"File: {group.Key}");
                foreach (var match in group)
                {
                    var preview = match.Content.Length > 100
                        ? match.Content[..100] + "..."
                        : match.Content;
                    sb.AppendLine($"  L{match.Line}: {preview}");
                }
            }

            if (results.Count >= maxResults)
            {
                sb.AppendLine($"--- (limited to {maxResults} results)");
            }

            return sb.ToString();
        }
        catch (RegexParseException ex)
        {
            return $"Error: Invalid regex pattern - {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error searching: {ex.Message}";
        }
    }
}

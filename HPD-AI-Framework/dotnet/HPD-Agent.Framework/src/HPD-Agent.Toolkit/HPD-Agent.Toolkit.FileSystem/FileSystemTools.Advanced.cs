using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace HPD.Agent.Toolkit.FileSystem;

/// <summary>
/// Advanced file operations: glob search, grep, and smart editing
/// </summary>
public partial class FileSystemTools
{
    #region Advanced Search Operations

    [AIFunction<FileSystemContext>]
    [ConditionalFunction("EnableSearch")]
    [AIDescription("Find files matching a glob pattern (e.g., '**/*.cs', '*.txt', 'src/**/*.json'). Only available when search is enabled.")]
    public Task<string> FindFiles(
        [AIDescription("Glob pattern to match files (e.g., '**/*.cs', 'src/**/*.json')")] string pattern,
        [AIDescription("Optional: Directory to search in (defaults to workspace root)")] string? searchPath = null)
    {
        if (!_context.EnableSearch)
            return Task.FromResult("Error: Search operations are disabled in this context.");

        // Validate pattern
        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult("Error: Pattern cannot be empty.");

        var basePath = searchPath ?? _context.WorkspaceRoot;

        // Validate path
        if (!Path.IsPathRooted(basePath))
            basePath = Path.Combine(_context.WorkspaceRoot, basePath);

        if (!_context.IsPathWithinWorkspace(basePath))
            return Task.FromResult($"Error: Search path must be within workspace: {_context.WorkspaceRoot}");

        if (!Directory.Exists(basePath))
            return Task.FromResult($"Error: Directory not found: {basePath}");

        try
        {
            // Use Microsoft.Extensions.FileSystemGlobbing
            var matcher = new Matcher();
            matcher.AddInclude(pattern);

            var result = matcher.Execute(
                new DirectoryInfoWrapper(new DirectoryInfo(basePath)));

            var matches = result.Files
                .Select(f => new FileInfo(Path.Combine(basePath, f.Path)))
                .ToList();

            // Apply gitignore filtering if enabled
            var filteredMatches = matches;
            int ignoredCount = 0;

            if (_gitIgnoreChecker != null)
            {
                var filtered = _gitIgnoreChecker.FilterIgnored(matches.Select(f => f.FullName))
                    .Select(path => new FileInfo(path))
                    .ToList();

                ignoredCount = matches.Count - filtered.Count;
                filteredMatches = filtered;
            }

            // Sort by most recently modified first
            var sortedMatches = filteredMatches
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            // Build result
            var sb = new StringBuilder();
            sb.AppendLine($"--- Found {sortedMatches.Count} files matching '{pattern}' ---");

            if (ignoredCount > 0)
            {
                sb.AppendLine($"(Filtered {ignoredCount} ignored files via .gitignore/.hpdignore)");
            }

            const int maxResults = 100;
            var displayCount = Math.Min(sortedMatches.Count, maxResults);

            for (int i = 0; i < displayCount; i++)
            {
                var file = sortedMatches[i];
                var relativePath = Path.GetRelativePath(_context.WorkspaceRoot, file.FullName);
                var size = FormatFileSize(file.Length);
                var age = FormatAge(DateTime.Now - file.LastWriteTime);

                sb.AppendLine($"{i + 1}. {relativePath} ({size}, modified {age})");
            }

            if (sortedMatches.Count > maxResults)
            {
                sb.AppendLine();
                sb.AppendLine($"... and {sortedMatches.Count - maxResults} more files (showing first {maxResults})");
            }

            return Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error finding files: {ex.Message}");
        }
    }

    [AIFunction<FileSystemContext>]
    [ConditionalFunction("EnableSearch")]
    [AIDescription("Search for a regular expression pattern within file contents (like grep). Only available when search is enabled.")]
    public async Task<string> SearchContent(
        [AIDescription("Regular expression pattern to search for")] string pattern,
        [AIDescription("Optional: Directory to search in (defaults to workspace root)")] string? searchPath = null,
        [AIDescription("Optional: File glob pattern to filter files (e.g., '*.cs', '*.txt')")] string? filePattern = null,
        [AIDescription("Whether the search should be case-sensitive")] bool caseSensitive = false)
    {
        if (!_context.EnableSearch)
            return "Error: Search operations are disabled in this context.";

        // Validate pattern
        if (string.IsNullOrWhiteSpace(pattern))
            return "Error: Search pattern cannot be empty.";

        var basePath = searchPath ?? _context.WorkspaceRoot;

        // Validate path
        if (!Path.IsPathRooted(basePath))
            basePath = Path.Combine(_context.WorkspaceRoot, basePath);

        if (!_context.IsPathWithinWorkspace(basePath))
            return $"Error: Search path must be within workspace: {_context.WorkspaceRoot}";

        if (!Directory.Exists(basePath))
            return $"Error: Directory not found: {basePath}";

        try
        {
            var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(pattern, regexOptions);

            var matches = new List<(string file, int lineNumber, string line)>();
            const int maxTotalMatches = 500;

            // Get files (with optional glob filter)
            IEnumerable<string> filesToSearch;
            if (!string.IsNullOrWhiteSpace(filePattern))
            {
                var matcher = new Matcher();
                matcher.AddInclude(filePattern);
                var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(basePath)));
                filesToSearch = result.Files.Select(f => Path.Combine(basePath, f.Path));
            }
            else
            {
                filesToSearch = Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories);
            }

            // Apply gitignore filtering if enabled
            if (_gitIgnoreChecker != null)
            {
                filesToSearch = _gitIgnoreChecker.FilterIgnored(filesToSearch);
            }

            foreach (var file in filesToSearch)
            {
                if (matches.Count >= maxTotalMatches)
                    break;

                // Skip binary files
                if (IsBinaryFile(file))
                    continue;

                // Search file content
                var lines = await File.ReadAllLinesAsync(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        var relativePath = Path.GetRelativePath(_context.WorkspaceRoot, file);
                        matches.Add((relativePath, i + 1, lines[i].Trim()));

                        if (matches.Count >= maxTotalMatches)
                            break;
                    }
                }
            }

            // Build result
            var sb = new StringBuilder();
            sb.AppendLine($"--- Found {matches.Count} matches for '{pattern}' ---");

            if (filePattern != null)
                sb.AppendLine($"File filter: {filePattern}");

            sb.AppendLine();

            // Group by file
            var groupedMatches = matches.GroupBy(m => m.file);

            foreach (var group in groupedMatches)
            {
                sb.AppendLine($"File: {group.Key}");
                foreach (var match in group)
                {
                    sb.AppendLine($"  Line {match.lineNumber}: {match.line}");
                }
                sb.AppendLine();
            }

            if (matches.Count == maxTotalMatches)
            {
                sb.AppendLine($"(Maximum of {maxTotalMatches} matches reached)");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching content: {ex.Message}";
        }
    }

    [AIFunction<FileSystemContext>]
    [ConditionalFunction("EnableSearch")]
    [AIDescription("Read and concatenate content from multiple files matching glob patterns. Useful for getting an overview of a codebase or analyzing multiple files. Only available when search is enabled.")]
    [RequiresPermission]
    public async Task<string> ReadManyFiles(
        [AIDescription("Array of glob patterns (e.g., '**/*.cs', '*.md', 'src/**/*.json')")] string[] patterns,
        [AIDescription("Optional: Array of glob patterns to exclude (e.g., '**/bin/**', '**/obj/**')")] string[]? exclude = null,
        [AIDescription("Optional: Directory to search in (defaults to workspace root)")] string? searchPath = null)
    {
        if (!_context.EnableSearch)
            return "Error: Search operations are disabled in this context.";

        // Validate patterns
        if (patterns == null || patterns.Length == 0)
            return "Error: At least one pattern must be provided.";

        var basePath = searchPath ?? _context.WorkspaceRoot;

        // Validate path
        if (!Path.IsPathRooted(basePath))
            basePath = Path.Combine(_context.WorkspaceRoot, basePath);

        if (!_context.IsPathWithinWorkspace(basePath))
            return $"Error: Search path must be within workspace: {_context.WorkspaceRoot}";

        if (!Directory.Exists(basePath))
            return $"Error: Directory not found: {basePath}";

        try
        {
            // Setup matcher
            var matcher = new Matcher();

            // Add include patterns
            foreach (var pattern in patterns)
            {
                if (!string.IsNullOrWhiteSpace(pattern))
                    matcher.AddInclude(pattern);
            }

            // Add exclude patterns
            if (exclude != null)
            {
                foreach (var pattern in exclude)
                {
                    if (!string.IsNullOrWhiteSpace(pattern))
                        matcher.AddExclude(pattern);
                }
            }

            // Add default excludes (common directories to skip)
            var defaultExcludes = new[] { "**/node_modules/**", "**/bin/**", "**/obj/**", "**/.git/**", "**/dist/**", "**/build/**" };
            foreach (var pattern in defaultExcludes)
            {
                matcher.AddExclude(pattern);
            }

            // Execute matching
            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(basePath)));

            var matchedFiles = result.Files
                .Select(f => Path.Combine(basePath, f.Path))
                .Where(f => _context.IsPathWithinWorkspace(f))
                .ToList();

            // Apply gitignore filtering if enabled
            if (_gitIgnoreChecker != null)
            {
                matchedFiles = _gitIgnoreChecker.FilterIgnored(matchedFiles).ToList();
            }

            if (matchedFiles.Count == 0)
            {
                return $"No files found matching patterns: {string.Join(", ", patterns)}";
            }

            // Limit to prevent overwhelming the context
            const int maxFiles = 50;
            var filesToRead = matchedFiles.Take(maxFiles).ToList();

            var skippedFiles = new List<string>();
            var contentParts = new List<string>();

            // Read files in parallel
            var readTasks = filesToRead.Select(async filePath =>
            {
                try
                {
                    // Skip binary files
                    if (IsBinaryFile(filePath))
                    {
                        return (filePath, content: (string?)null, error: "binary file");
                    }

                    // Read file
                    var fileInfo = new FileInfo(filePath);

                    // Skip files that are too large
                    if (fileInfo.Length > _context.MaxFileSize)
                    {
                        return (filePath, content: (string?)null, error: $"file too large ({FormatFileSize(fileInfo.Length)})");
                    }

                    var content = await File.ReadAllTextAsync(filePath);
                    return (filePath, content, error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (filePath, content: (string?)null, error: ex.Message);
                }
            });

            var results = await Task.WhenAll(readTasks);

            // Build concatenated output
            foreach (var (filePath, content, error) in results)
            {
                var relativePath = Path.GetRelativePath(_context.WorkspaceRoot, filePath);

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

            // Build result message
            var sb = new StringBuilder();
            sb.AppendLine($"=== Read {contentParts.Count} file(s) matching patterns: {string.Join(", ", patterns)} ===");
            sb.AppendLine();

            if (skippedFiles.Any())
            {
                sb.AppendLine($"Skipped {skippedFiles.Count} file(s):");
                foreach (var skipped in skippedFiles.Take(10))
                {
                    sb.AppendLine($"  - {skipped}");
                }
                if (skippedFiles.Count > 10)
                {
                    sb.AppendLine($"  ... and {skippedFiles.Count - 10} more");
                }
                sb.AppendLine();
            }

            if (matchedFiles.Count > maxFiles)
            {
                sb.AppendLine($"Note: Showing first {maxFiles} of {matchedFiles.Count} matching files.");
                sb.AppendLine();
            }

            // Add file contents
            foreach (var part in contentParts)
            {
                sb.AppendLine(part);
            }

            sb.AppendLine("--- End of content ---");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading multiple files: {ex.Message}";
        }
    }

    #endregion

    #region Advanced Edit Operations

    [AIFunction<FileSystemContext>]
    [AIDescription("Edit a file by replacing old_string with new_string. Uses smart matching strategies (exact, flexible whitespace, regex fuzzy). Shows a diff preview of changes.")]
    [RequiresPermission]
    public async Task<string> EditFile(
        [AIDescription("The absolute path to the file to edit")] string filePath,
        [AIDescription("The exact string to find and replace (will try smart matching if exact match fails)")] string oldString,
        [AIDescription("The new string to replace with")] string newString)
    {
        // Validate path
        if (string.IsNullOrWhiteSpace(filePath))
            return "Error: File path cannot be empty.";

        if (!Path.IsPathRooted(filePath))
            return $"Error: Path must be absolute, but was relative: {filePath}";

        if (!_context.IsPathWithinWorkspace(filePath))
            return $"Error: File path must be within workspace: {_context.WorkspaceRoot}";

        if (!File.Exists(filePath))
            return $"Error: File not found: {filePath}";

        if (string.IsNullOrEmpty(oldString))
            return "Error: old_string cannot be empty.";

        try
        {
            // Read current content
            var currentContent = await File.ReadAllTextAsync(filePath);

            // Try smart replacement strategies
            var replacementResult = CalculateSmartReplacement(currentContent, oldString, newString);

            // Validate occurrences
            if (replacementResult.occurrences == 0)
            {
                return $"Error: Could not find the specified text in the file.{Environment.NewLine}" +
                       $"Looking for: {oldString.Substring(0, Math.Min(100, oldString.Length))}...{Environment.NewLine}" +
                       $"Tried: exact match, flexible whitespace matching, and regex fuzzy matching.";
            }

            if (replacementResult.occurrences > 1)
            {
                return $"Error: Found {replacementResult.occurrences} occurrences of the text. The old_string must be unique in the file. Please provide more context to make it unique.";
            }

            var newContent = replacementResult.newContent;

            // Generate diff
            var diffBuilder = new InlineDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(currentContent, newContent);

            // Generate diff display
            var diffDisplay = GenerateDiffDisplay(diff);

            // Write file
            await File.WriteAllTextAsync(filePath, newContent, Encoding.UTF8);

            // Get stats
            var linesChanged = diff.Lines.Count(l => l.Type != ChangeType.Unchanged);

            return $"✓ File edited successfully: {filePath}{Environment.NewLine}" +
                   $"Strategy: {replacementResult.strategy}{Environment.NewLine}" +
                   $"Changed {linesChanged} lines{Environment.NewLine}{Environment.NewLine}" +
                   $"--- Diff ---{Environment.NewLine}{diffDisplay}";
        }
        catch (Exception ex)
        {
            return $"Error editing file: {ex.Message}";
        }
    }

    #endregion

    #region Smart Edit Helper Methods

    /// <summary>
    /// Result of a smart replacement operation
    /// </summary>
    private record SmartReplacementResult(string newContent, int occurrences, string strategy);

    /// <summary>
    /// Tries multiple strategies to find and replace text: exact, flexible (whitespace-insensitive), and regex fuzzy
    /// </summary>
    private SmartReplacementResult CalculateSmartReplacement(string currentContent, string oldString, string newString)
    {
        // Normalize line endings to \n for consistent processing
        var normalizedContent = currentContent.Replace("\r\n", "\n");
        var normalizedOldString = oldString.Replace("\r\n", "\n");
        var normalizedNewString = newString.Replace("\r\n", "\n");

        // Strategy 1: Exact match
        var exactOccurrences = CountOccurrences(normalizedContent, normalizedOldString);
        if (exactOccurrences > 0)
        {
            var result = normalizedContent.Replace(normalizedOldString, normalizedNewString);
            return new SmartReplacementResult(result, exactOccurrences, "exact match");
        }

        // Strategy 2: Flexible match (ignores whitespace differences and indentation)
        var flexibleResult = FlexibleReplace(normalizedContent, normalizedOldString, normalizedNewString);
        if (flexibleResult.occurrences > 0)
        {
            return new SmartReplacementResult(flexibleResult.newContent, flexibleResult.occurrences, "flexible whitespace match");
        }

        // Strategy 3: Regex fuzzy match (tokenizes and allows flexible whitespace)
        var regexResult = RegexFuzzyReplace(normalizedContent, normalizedOldString, normalizedNewString);
        if (regexResult.occurrences > 0)
        {
            return new SmartReplacementResult(regexResult.newContent, regexResult.occurrences, "regex fuzzy match");
        }

        // No matches found
        return new SmartReplacementResult(currentContent, 0, "no match");
    }

    /// <summary>
    /// Flexible replacement that ignores indentation differences
    /// </summary>
    private (string newContent, int occurrences) FlexibleReplace(string content, string search, string replace)
    {
        var sourceLines = content.Split('\n');
        var searchLines = search.Split('\n').Select(l => l.Trim()).ToArray();
        var replaceLines = replace.Split('\n');

        if (searchLines.Length == 0)
            return (content, 0);

        int occurrences = 0;
        int i = 0;

        while (i <= sourceLines.Length - searchLines.Length)
        {
            var window = sourceLines.Skip(i).Take(searchLines.Length).ToArray();
            var windowStripped = window.Select(l => l.Trim()).ToArray();

            // Check if this window matches the search pattern (ignoring whitespace)
            if (windowStripped.SequenceEqual(searchLines))
            {
                occurrences++;

                // Preserve the indentation of the first line
                var firstLineIndentation = GetIndentation(window[0]);
                var indentedReplace = replaceLines.Select(line => firstLineIndentation + line.TrimStart());

                // Replace this section
                var before = sourceLines.Take(i);
                var after = sourceLines.Skip(i + searchLines.Length);
                sourceLines = before.Concat(indentedReplace).Concat(after).ToArray();

                i += replaceLines.Length;
            }
            else
            {
                i++;
            }
        }

        return (string.Join("\n", sourceLines), occurrences);
    }

    /// <summary>
    /// Regex-based fuzzy matching - tokenizes the search string and allows flexible whitespace
    /// </summary>
    private (string newContent, int occurrences) RegexFuzzyReplace(string content, string search, string replace)
    {
        // Delimiters to split on
        var delimiters = new[] { '(', ')', ':', '[', ']', '{', '}', '>', '<', '=' };

        // Process search string: add spaces around delimiters
        var processedSearch = search;
        foreach (var delim in delimiters)
        {
            processedSearch = processedSearch.Replace(delim.ToString(), $" {delim} ");
        }

        // Split by whitespace and filter empty tokens
        var tokens = processedSearch.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return (content, 0);

        // Escape each token for regex
        var escapedTokens = tokens.Select(Regex.Escape);

        // Join tokens with flexible whitespace pattern
        var pattern = string.Join(@"\s*", escapedTokens);

        // Build final pattern: capture leading whitespace (indentation) and match tokens
        var finalPattern = @"^(\s*)" + pattern;

        try
        {
            var regex = new Regex(finalPattern, RegexOptions.Multiline);
            var match = regex.Match(content);

            if (!match.Success)
                return (content, 0);

            // Extract indentation from the match
            var indentation = match.Groups[1].Value;

            // Apply indentation to replacement lines
            var replaceLines = replace.Split('\n');
            var indentedReplace = string.Join("\n", replaceLines.Select(line => indentation + line.TrimStart()));

            // Replace only the first occurrence
            var result = regex.Replace(content, indentedReplace, 1);

            return (result, 1);
        }
        catch (ArgumentException)
        {
            // Regex pattern failed - return no match
            return (content, 0);
        }
    }

    /// <summary>
    /// Counts occurrences of a substring in a string
    /// </summary>
    private static int CountOccurrences(string str, string substr)
    {
        if (string.IsNullOrEmpty(substr))
            return 0;

        int count = 0;
        int pos = str.IndexOf(substr, StringComparison.Ordinal);
        while (pos != -1)
        {
            count++;
            pos = str.IndexOf(substr, pos + substr.Length, StringComparison.Ordinal);
        }
        return count;
    }

    /// <summary>
    /// Gets the leading whitespace from a string
    /// </summary>
    private static string GetIndentation(string line)
    {
        var match = Regex.Match(line, @"^(\s*)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    #endregion

    #region Helper Methods

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
            return "just now";
        if (age.TotalHours < 1)
            return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1)
            return $"{(int)age.TotalHours}h ago";
        if (age.TotalDays < 7)
            return $"{(int)age.TotalDays}d ago";
        if (age.TotalDays < 30)
            return $"{(int)(age.TotalDays / 7)}w ago";
        return $"{(int)(age.TotalDays / 30)}mo ago";
    }

    private static bool IsBinaryFile(string filePath)
    {
        try
        {
            // Check extension first
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var binaryExtensions = new[] { ".exe", ".dll", ".bin", ".dat", ".zip", ".tar", ".gz", ".jpg", ".png", ".gif", ".pdf", ".mp3", ".mp4" };
            if (binaryExtensions.Contains(ext))
                return true;

            // Read first 8KB and check for null bytes
            using var fs = File.OpenRead(filePath);
            var buffer = new byte[8192];
            var bytesRead = fs.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }

            return false;
        }
        catch
        {
            return true; // Assume binary if can't read
        }
    }

    private static string GenerateDiffDisplay(DiffPaneModel diff)
    {
        var sb = new StringBuilder();
        const int contextLines = 3;
        const int maxDisplayLines = 50;

        var changedLineIndices = diff.Lines
            .Select((line, index) => (line, index))
            .Where(x => x.line.Type != ChangeType.Unchanged)
            .Select(x => x.index)
            .ToList();

        if (!changedLineIndices.Any())
            return "(no changes)";

        // Find ranges to display
        var ranges = new List<(int start, int end)>();
        foreach (var idx in changedLineIndices)
        {
            var start = Math.Max(0, idx - contextLines);
            var end = Math.Min(diff.Lines.Count - 1, idx + contextLines);

            if (ranges.Any() && start <= ranges.Last().end + 1)
            {
                ranges[^1] = (ranges.Last().start, end);
            }
            else
            {
                ranges.Add((start, end));
            }
        }

        var displayedLines = 0;
        foreach (var (start, end) in ranges)
        {
            if (displayedLines >= maxDisplayLines)
            {
                sb.AppendLine("... (more changes not shown)");
                break;
            }

            for (int i = start; i <= end && displayedLines < maxDisplayLines; i++)
            {
                var line = diff.Lines[i];
                var prefix = line.Type switch
                {
                    ChangeType.Inserted => "+ ",
                    ChangeType.Deleted => "- ",
                    ChangeType.Modified => "! ",
                    _ => "  "
                };

                sb.AppendLine($"{prefix}{line.Text}");
                displayedLines++;
            }

            if (ranges.Count > 1 && (start, end) != ranges.Last())
            {
                sb.AppendLine("...");
            }
        }

        return sb.ToString();
    }

    #endregion
}

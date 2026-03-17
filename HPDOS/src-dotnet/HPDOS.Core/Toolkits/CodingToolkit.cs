using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using HPD.Agent;
using HPDOS.Toolkits.Middleware;
using MAB.DotIgnore;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Ude;

using Matcher = Microsoft.Extensions.FileSystemGlobbing.Matcher;

/// <summary>
/// CodingToolkit - Comprehensive coding assistant with file operations, search, execution, and analysis.
/// Features: Line-based reading, smart diff-based editing, glob patterns, .gitignore support, grep search, shell execution.
/// </summary>
[Collapse(
    "Contains tools for coding operations: file operations, code search, shell execution, and code analysis.",
    Middlewares = [typeof(EnvironmentContextMiddleware)])]
public class CodingToolkit
{
    private readonly IgnoreList? _gitIgnoreList = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), ".gitignore"))
        ? new IgnoreList(Path.Combine(Directory.GetCurrentDirectory(), ".gitignore"))
        : null;
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bin", ".obj", ".o",
        ".zip", ".tar", ".gz", ".rar", ".7z",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp",
        ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".woff", ".woff2", ".ttf", ".eot",
        ".pyc", ".class", ".pdb"
    };

    private static readonly HashSet<string> DefaultIgnoreDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea",
        "__pycache__", "venv", ".venv", "dist", "build", "target",
        ".next", ".nuxt", "coverage", ".cache"
    };


    // ═══════════════════════════════════════════════════════════════════
    // READ FILE - With offset/limit and encoding detection
    // ═══════════════════════════════════════════════════════════════════

    [AIFunction]
    [AIDescription("Read file contents with optional line offset and limit. Returns file content with line numbers. Automatically detects file encoding.")]
    public string ReadFile(
        [AIDescription("Absolute path to the file to read.")] string filePath,
        [AIDescription("Line number to start reading from (1-based). Default: 1")] int offset = 1,
        [AIDescription("Maximum number of lines to read. Default: 500 (0 = all lines)")] int limit = 500)
    {
        if (!File.Exists(filePath))
            return $"Error: File not found: {filePath}";

        // Check if binary
        var ext = Path.GetExtension(filePath);
        if (BinaryExtensions.Contains(ext))
            return $"Error: Cannot read binary file ({ext}). Use appropriate tool for binary files.";

        try
        {
            // Detect encoding
            var encoding = DetectEncoding(filePath) ?? Encoding.UTF8;
            var lines = File.ReadAllLines(filePath, encoding);
            var totalLines = lines.Length;

            // Validate offset
            if (offset < 1) offset = 1;
            if (offset > totalLines)
                return $"Error: Offset {offset} exceeds file length ({totalLines} lines).";

            // Calculate range
            var startIndex = offset - 1;
            var endIndex = limit > 0
                ? Math.Min(startIndex + limit, totalLines)
                : totalLines;
            var linesRead = endIndex - startIndex;

            // Build output with line numbers
            var sb = new StringBuilder();
            var mimeType = GetMimeType(ext);

            sb.AppendLine($"File: {Path.GetFileName(filePath)}");
            sb.AppendLine($"Type: {mimeType}");
            sb.AppendLine($"Lines: {offset}-{offset + linesRead - 1} of {totalLines}");
            sb.AppendLine("---");

            for (var i = startIndex; i < endIndex; i++)
            {
                sb.AppendLine($"{i + 1,4}│ {lines[i]}");
            }

            // Add truncation notice if needed
            if (endIndex < totalLines)
            {
                sb.AppendLine("---");
                sb.AppendLine($"TRUNCATED: Showing {linesRead} of {totalLines} lines.");
                sb.AppendLine($"To read more, use offset: {endIndex + 1}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // READ MANY FILES - Batch read with glob patterns
    // ═══════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════
    // EDIT FILE - Smart matching (exact, flexible whitespace, regex fuzzy)
    // ═══════════════════════════════════════════════════════════════════

    [AIFunction]
    [AIDescription("Edit a file by replacing exact string matches. Uses smart matching: tries exact match first, then flexible whitespace matching, then regex fuzzy matching. PREFERRED over WriteFile for targeted changes.")]
    public string EditFile(
        [AIDescription("Absolute path to the file to edit.")] string filePath,
        [AIDescription("Exact string to find and replace (will try smart matching if exact match fails).")] string oldString,
        [AIDescription("New string to replace with.")] string newString,
        [AIDescription("Replace all occurrences (true) or just first (false). Default: false")] bool replaceAll = false)
    {
        if (!File.Exists(filePath))
            return $"Error: File not found: {filePath}";

        if (string.IsNullOrEmpty(oldString))
            return "Error: oldString cannot be empty";

        if (oldString == newString)
            return "Error: oldString and newString are identical - no changes needed";

        try
        {
            var content = File.ReadAllText(filePath);

            // Use smart replacement
            var replacementResult = CalculateSmartReplacement(content, oldString, newString, replaceAll);

            if (replacementResult.Occurrences == 0)
            {
                return $"Error: Could not find the specified text in the file.\n" +
                       $"Looking for: {oldString[..Math.Min(100, oldString.Length)]}...\n" +
                       $"Tried: exact match, flexible whitespace matching, and regex fuzzy matching.";
            }

            if (!replaceAll && replacementResult.Occurrences > 1 && replacementResult.Strategy == "exact match")
            {
                return $"Error: Found {replacementResult.Occurrences} occurrences of the text. " +
                       $"Either set replaceAll=true or provide more context to make the match unique.";
            }

            var newContent = replacementResult.NewContent;

            // Generate diff for preview
            var diffBuilder = new InlineDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(content, newContent);

            var additions = diff.Lines.Count(l => l.Type == ChangeType.Inserted);
            var deletions = diff.Lines.Count(l => l.Type == ChangeType.Deleted);

            var sb = new StringBuilder();

            sb.AppendLine($"Editing: {filePath}");
            sb.AppendLine($"Strategy: {replacementResult.Strategy}");
            sb.AppendLine($"Replacements: {replacementResult.Occurrences} occurrence(s)");
            sb.AppendLine($"Changes: +{additions} -{deletions} lines");
            sb.AppendLine("---");

            // Show diff (condensed)
            sb.Append(GenerateDiffDisplay(diff));

            // Write the file
            File.WriteAllText(filePath, newContent);

            sb.AppendLine("---");
            sb.AppendLine($"✓ Successfully edited {filePath}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error editing file: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // WRITE FILE - With diff preview
    // ═══════════════════════════════════════════════════════════════════

    [AIFunction]
    [AIDescription("Write content to a file. Shows diff if file exists. Creates directories if needed.")]
    public string WriteFile(
        [AIDescription("Absolute path to the file to write.")] string filePath,
        [AIDescription("Content to write to the file.")] string content)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var sb = new StringBuilder();
            var fileExists = File.Exists(filePath);

            if (fileExists)
            {
                var originalContent = File.ReadAllText(filePath);

                // Generate diff
                var diffBuilder = new InlineDiffBuilder(new Differ());
                var diff = diffBuilder.BuildDiffModel(originalContent, content);

                var additions = diff.Lines.Count(l => l.Type == ChangeType.Inserted);
                var deletions = diff.Lines.Count(l => l.Type == ChangeType.Deleted);

                sb.AppendLine($"Modifying: {filePath}");
                sb.AppendLine($"Changes: +{additions} -{deletions} lines");
                sb.AppendLine("---");

                // Show condensed diff
                sb.Append(GenerateDiffDisplay(diff, maxLines: 50));
            }
            else
            {
                sb.AppendLine($"Creating: {filePath}");
                sb.AppendLine($"Size: {content.Length} characters, {content.Split('\n').Length} lines");
            }

            // Write the file
            File.WriteAllText(filePath, content);

            sb.AppendLine("---");
            sb.AppendLine($"✓ Successfully wrote to {filePath}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // LIST DIRECTORY - With .gitignore support
    // ═══════════════════════════════════════════════════════════════════

    [AIFunction]
    [AIDescription("List directory contents with file metadata. Respects .gitignore patterns.")]
    public string ListDirectory(
        [AIDescription("Absolute path to the directory to list. If empty, uses current working directory.")] string directoryPath = "",
        [AIDescription("Include hidden files/directories. Default: false")] bool showHidden = false)
    {
        // Default to current directory if not provided
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            directoryPath = Directory.GetCurrentDirectory();
        }

        if (!Directory.Exists(directoryPath))
            return $"Error: Directory not found: {directoryPath}";

        try
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Directory: {directoryPath}");
            sb.AppendLine("---");

            // List directories first
            var dirs = Directory.GetDirectories(directoryPath)
                .Select(d => new DirectoryInfo(d))
                .Where(d => showHidden || !d.Name.StartsWith('.'))
                .Where(d => !DefaultIgnoreDirs.Contains(d.Name))
                .OrderBy(d => d.Name);

            foreach (var dir in dirs)
            {
                sb.AppendLine($"▸ {dir.Name}/");
            }

            // List files
            var files = Directory.GetFiles(directoryPath)
                .Select(f => new FileInfo(f))
                .Where(f => showHidden || !f.Name.StartsWith('.'))
                .OrderBy(f => f.Name);

            // Apply gitignore filtering
            var filteredFiles = FilterIgnoredFiles(files.Select(f => f.FullName), directoryPath)
                .Select(p => new FileInfo(p));

            foreach (var file in filteredFiles)
            {
                var size = FormatFileSize(file.Length);
                var modified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                sb.AppendLine($"📄 {file.Name,-40} {size,10} {modified}");
            }

            var dirCount = dirs.Count();
            var fileCount = filteredFiles.Count();

            sb.AppendLine("---");
            sb.AppendLine($"Total: {dirCount} directories, {fileCount} files");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing directory: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // GLOB SEARCH - Using Microsoft.Extensions.FileSystemGlobbing
    // ═══════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════
    // GREP - Content search with regex support
    // ═══════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════
    // DIFF FILES - Compare two files
    // ═══════════════════════════════════════════════════════════════════

    [AIFunction]
    [AIDescription("Compare two files and show differences.")]
    public string DiffFiles(
        [AIDescription("Path to the original file.")] string originalPath,
        [AIDescription("Path to the modified file.")] string modifiedPath)
    {
        if (!File.Exists(originalPath))
            return $"Error: Original file not found: {originalPath}";
        if (!File.Exists(modifiedPath))
            return $"Error: Modified file not found: {modifiedPath}";

        try
        {
            var original = File.ReadAllText(originalPath);
            var modified = File.ReadAllText(modifiedPath);

            var diffBuilder = new SideBySideDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(original, modified);

            var sb = new StringBuilder();

            sb.AppendLine($"Diff: {Path.GetFileName(originalPath)} ↔ {Path.GetFileName(modifiedPath)}");
            sb.AppendLine("---");

            var lineNum = 0;
            foreach (var (oldLine, newLine) in diff.OldText.Lines.Zip(diff.NewText.Lines))
            {
                lineNum++;
                if (oldLine.Type == ChangeType.Unchanged && newLine.Type == ChangeType.Unchanged)
                    continue;

                if (oldLine.Type == ChangeType.Deleted)
                    sb.AppendLine($"-{lineNum,4}│ {oldLine.Text}");
                if (newLine.Type == ChangeType.Inserted)
                    sb.AppendLine($"+{lineNum,4}│ {newLine.Text}");
                if (oldLine.Type == ChangeType.Modified)
                {
                    sb.AppendLine($"-{lineNum,4}│ {oldLine.Text}");
                    sb.AppendLine($"+{lineNum,4}│ {newLine.Text}");
                }
            }

            return sb.Length > 0 ? sb.ToString() : "Files are identical.";
        }
        catch (Exception ex)
        {
            return $"Error comparing files: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // FILE INFO - Get detailed file metadata
    // ═══════════════════════════════════════════════════════════════════

    [AIFunction]
    [AIDescription("Get detailed information about a file.")]
    public string GetFileInfo(
        [AIDescription("Absolute path to the file.")] string filePath)
    {
        if (!File.Exists(filePath))
            return $"Error: File not found: {filePath}";

        try
        {
            var info = new FileInfo(filePath);
            var ext = info.Extension;
            var mimeType = GetMimeType(ext);
            var isBinary = BinaryExtensions.Contains(ext);

            var sb = new StringBuilder();
            sb.AppendLine($"File: {info.Name}");
            sb.AppendLine($"Path: {info.FullName}");
            sb.AppendLine($"Size: {FormatFileSize(info.Length)} ({info.Length:N0} bytes)");
            sb.AppendLine($"Type: {mimeType}");
            sb.AppendLine($"Binary: {isBinary}");
            sb.AppendLine($"Created: {info.CreationTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Accessed: {info.LastAccessTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ReadOnly: {info.IsReadOnly}");

            if (!isBinary)
            {
                var lines = File.ReadAllLines(filePath);
                sb.AppendLine($"Lines: {lines.Length}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting file info: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // EXECUTE COMMAND - Cross-platform shell execution
    // ═══════════════════════════════════════════════════════════════════

    [AIFunction]
    [AIDescription("Execute a shell command and return its output. Use for build, test, package management, and running scripts.")]
    public async Task<string> ExecuteCommand(
        [AIDescription("Command to execute (e.g., 'dotnet build', 'npm test', 'git status')")] string command,
        [AIDescription("Working directory for command execution. Default: current directory")] string workingDirectory = "",
        [AIDescription("Timeout in milliseconds. Default: 120000 (2 minutes)")] int timeout = 120000)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: Command cannot be empty";

        // Use current directory if not specified
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : workingDirectory;

        if (!Directory.Exists(workDir))
            return $"Error: Working directory not found: {workDir}";

        try
        {
            var (shell, shellArg) = GetShellExecutable();

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"{shellArg} \"{command.Replace("\"", "\\\"")}\"",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var output = new StringBuilder();
            var error = new StringBuilder();

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null) output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null) error.AppendLine(e.Data);
            };

            var sw = Stopwatch.StartNew();
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(timeout));
            sw.Stop();

            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { }

                return FormatCommandResult(
                    command: command,
                    workingDir: workDir,
                    exitCode: -1,
                    output: output.ToString(),
                    error: $"Command timed out after {timeout}ms",
                    duration: sw.ElapsedMilliseconds,
                    timedOut: true
                );
            }

            return FormatCommandResult(
                command: command,
                workingDir: workDir,
                exitCode: process.ExitCode,
                output: output.ToString(),
                error: error.ToString(),
                duration: sw.ElapsedMilliseconds,
                timedOut: false
            );
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}\n" +
                   $"Command: {command}\n" +
                   $"Working Directory: {workDir}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SMART EDIT HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private record SmartReplacementResult(string NewContent, int Occurrences, string Strategy);

    /// <summary>
    /// Tries multiple strategies to find and replace text: exact, flexible (whitespace-insensitive), and regex fuzzy
    /// </summary>
    private SmartReplacementResult CalculateSmartReplacement(string currentContent, string oldString, string newString, bool replaceAll)
    {
        // Normalize line endings to \n for consistent processing
        var normalizedContent = currentContent.Replace("\r\n", "\n");
        var normalizedOldString = oldString.Replace("\r\n", "\n");
        var normalizedNewString = newString.Replace("\r\n", "\n");

        // Strategy 1: Exact match
        var exactOccurrences = CountOccurrences(normalizedContent, normalizedOldString);
        if (exactOccurrences > 0)
        {
            string result;
            if (replaceAll)
            {
                result = normalizedContent.Replace(normalizedOldString, normalizedNewString);
            }
            else
            {
                result = ReplaceFirst(normalizedContent, normalizedOldString, normalizedNewString);
            }
            return new SmartReplacementResult(result, exactOccurrences, "exact match");
        }

        // Strategy 2: Flexible match (ignores whitespace differences and indentation)
        var flexibleResult = FlexibleReplace(normalizedContent, normalizedOldString, normalizedNewString, replaceAll);
        if (flexibleResult.Occurrences > 0)
        {
            return new SmartReplacementResult(flexibleResult.NewContent, flexibleResult.Occurrences, "flexible whitespace match");
        }

        // Strategy 3: Regex fuzzy match (tokenizes and allows flexible whitespace)
        var regexResult = RegexFuzzyReplace(normalizedContent, normalizedOldString, normalizedNewString);
        if (regexResult.Occurrences > 0)
        {
            return new SmartReplacementResult(regexResult.NewContent, regexResult.Occurrences, "regex fuzzy match");
        }

        // No matches found
        return new SmartReplacementResult(currentContent, 0, "no match");
    }

    /// <summary>
    /// Flexible replacement that ignores indentation differences
    /// </summary>
    private (string NewContent, int Occurrences) FlexibleReplace(string content, string search, string replace, bool replaceAll)
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

                if (!replaceAll)
                    break;
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
    private (string NewContent, int Occurrences) RegexFuzzyReplace(string content, string search, string replace)
    {
        var delimiters = new[] { '(', ')', ':', '[', ']', '{', '}', '>', '<', '=' };

        var processedSearch = search;
        foreach (var delim in delimiters)
        {
            processedSearch = processedSearch.Replace(delim.ToString(), $" {delim} ");
        }

        var tokens = processedSearch.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return (content, 0);

        var escapedTokens = tokens.Select(Regex.Escape);
        var pattern = string.Join(@"\s*", escapedTokens);
        var finalPattern = @"^(\s*)" + pattern;

        try
        {
            var regex = new Regex(finalPattern, RegexOptions.Multiline);
            var match = regex.Match(content);

            if (!match.Success)
                return (content, 0);

            var indentation = match.Groups[1].Value;
            var replaceLines = replace.Split('\n');
            var indentedReplace = string.Join("\n", replaceLines.Select(line => indentation + line.TrimStart()));

            var result = regex.Replace(content, indentedReplace, 1);

            return (result, 1);
        }
        catch (ArgumentException)
        {
            return (content, 0);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════

    private static int CountOccurrences(string text, string substring)
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

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        int pos = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (pos < 0)
            return text;

        return text[..pos] + newValue + text[(pos + oldValue.Length)..];
    }

    private static string GetIndentation(string line)
    {
        var match = Regex.Match(line, @"^(\s*)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string FormatFileSize(long bytes)
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

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
        if (age.TotalDays < 7) return $"{(int)age.TotalDays}d ago";
        if (age.TotalDays < 30) return $"{(int)(age.TotalDays / 7)}w ago";
        return $"{(int)(age.TotalDays / 30)}mo ago";
    }

    private static bool IsBinaryFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (BinaryExtensions.Contains(ext))
            return true;

        try
        {
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
            return true;
        }
    }

    private static Encoding? DetectEncoding(string filePath)
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

    private static string GetMimeType(string extension)
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

    private IEnumerable<string> FilterIgnoredFiles(IEnumerable<string> files, string rootPath)
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

    private static string GenerateDiffDisplay(DiffPaneModel diff, int maxLines = 30)
    {
        var sb = new StringBuilder();
        const int contextLines = 3;

        var changedLineIndices = diff.Lines
            .Select((line, index) => (line, index))
            .Where(x => x.line.Type != ChangeType.Unchanged)
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
                    ChangeType.Inserted => "+ ",
                    ChangeType.Deleted => "- ",
                    ChangeType.Modified => "! ",
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
    private static (string shell, string args) GetShellExecutable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("cmd.exe", "/c");

        return (Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash", "-c");
    }

    private static string FormatCommandResult(string command, string workingDir, int exitCode, string output, string error, long duration, bool timedOut)
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

    // ========== OpenAPI Sources ==========

    // [OpenApi(Prefix = "github")]
    // public OpenApiConfig GitHub() => new()
    // {
    //     SpecUri = new Uri("https://raw.githubusercontent.com/github/rest-api-description/main/descriptions/api.github.com/api.github.com.json"),
    //     IgnoreNonCompliantErrors = true,
    //     CollapseWithinToolkit = true,
    //     ResponseOptimization = new ResponseOptimizationConfig { MaxLength = 4000 },
    //     // Keep only the most commonly useful GitHub operations
    //     OperationSelectionPredicate = ctx => ctx.Id is
    //         "repos/get" or
    //         "repos/list-for-authenticated-user" or
    //         "repos/list-for-org" or
    //         "issues/list-for-repo" or
    //         "issues/get" or
    //         "issues/create" or
    //         "issues/create-comment" or
    //         "pulls/list" or
    //         "pulls/get" or
    //         "pulls/create" or
    //         "pulls/list-review-comments" or
    //         "git/get-tree" or
    //         "repos/get-content" or
    //         "repos/create-or-update-file-contents" or
    //         "search/repositories" or
    //         "search/issues-and-pull-requests" or
    //         "search/code" or
    //         "users/get-authenticated" or
    //         "orgs/list-for-authenticated-user",
    //     AuthCallback = (req, ct) =>
    //     {
    //         var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    //         if (!string.IsNullOrEmpty(token))
    //             req.Headers.Authorization = new("Bearer", token);
    //         return Task.CompletedTask;
    //     }
    // };

    // ========== MCP Servers ==========

    // [MCPServer(CollapseWithinToolkit = true)]
    // public static MCPServerConfig Context7Server() => new MCPServerConfig
    // {
    //     Name = "context7",
    //     Command = "npx",
    //     Arguments = new List<string> { "-y", "@upstash/context7-mcp" },
    //     Description = "Up-to-date library documentation and code examples from Context7",
    //     TimeoutMs = 30000,
    //     RetryAttempts = 3,
    //     Environment = new Dictionary<string, string> { ["NODE_ENV"] = "production" }
    // };

    private static string TruncateOutput(string text, int maxLines)
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

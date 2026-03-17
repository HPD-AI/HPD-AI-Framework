using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.FileSystemGlobbing;
using Ude;

namespace HPD.Agent.Toolkit.FileSystem;

/// <summary>
/// File system operations Toolkit for HPD-Agent.
/// Provides AI functions for reading, writing, editing, searching, and discovering files.
/// Inspired by Gemini CLI's file system tools.
/// </summary>
public partial class FileSystemTools
{
    private readonly FileSystemContext _context;
    private readonly GitIgnoreChecker? _gitIgnoreChecker;

    /// <summary>
    /// Creates a new FileSystemTools with default context (current directory, search enabled)
    /// </summary>
    public FileSystemTools()
        : this(new FileSystemContext(Directory.GetCurrentDirectory()))
    {
    }

    /// <summary>
    /// Creates a new FileSystemTools with a specific context
    /// </summary>
    public FileSystemTools(FileSystemContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // Initialize GitIgnoreChecker if gitignore support is enabled
        if (_context.RespectGitIgnore || _context.RespectGeminiIgnore)
        {
            _gitIgnoreChecker = new GitIgnoreChecker(
                _context.WorkspaceRoot,
                _context.RespectGitIgnore,
                _context.RespectGeminiIgnore);
        }
    }

    #region Core File Operations

    [AIFunction<FileSystemContext>]
    [AIDescription("Reads and returns the content of a specified file. Handles text files with optional line range. Maximum file size: {metadata.MaxFileSize} bytes.")]
    public async Task<string> ReadFile(
        [AIDescription("The absolute path to the file to read")] string absolutePath,
        [AIDescription("Optional: For text files, the 0-based line number to start reading from")] int? offset = null,
        [AIDescription("Optional: For text files, maximum number of lines to read")] int? limit = null)
    {
        // Validate path
        if (string.IsNullOrWhiteSpace(absolutePath))
            return "Error: File path cannot be empty.";

        if (!Path.IsPathRooted(absolutePath))
            return $"Error: Path must be absolute, but was relative: {absolutePath}";

        if (!_context.IsPathWithinWorkspace(absolutePath))
            return $"Error: File path must be within workspace: {_context.WorkspaceRoot}";

        if (!File.Exists(absolutePath))
            return $"Error: File not found: {absolutePath}";

        try
        {
            var fileInfo = new FileInfo(absolutePath);

            // Check file size
            if (fileInfo.Length > _context.MaxFileSize)
                return $"Error: File too large ({fileInfo.Length} bytes). Maximum size: {_context.MaxFileSize} bytes. Use offset/limit parameters for large files.";

            // Detect encoding
            var encoding = DetectEncoding(absolutePath) ?? Encoding.UTF8;

            // Read file
            var lines = await File.ReadAllLinesAsync(absolutePath, encoding);

            // Apply offset and limit if specified
            if (offset.HasValue || limit.HasValue)
            {
                int start = offset ?? 0;
                int count = limit ?? (lines.Length - start);

                if (start < 0 || start >= lines.Length)
                    return $"Error: Offset {start} is out of range. File has {lines.Length} lines.";

                var selectedLines = lines.Skip(start).Take(count).ToArray();
                var result = string.Join(Environment.NewLine, selectedLines);

                return $"--- File: {absolutePath} (lines {start + 1}-{start + selectedLines.Length} of {lines.Length}) ---{Environment.NewLine}{result}";
            }

            // Return full file
            var content = string.Join(Environment.NewLine, lines);
            return $"--- File: {absolutePath} ({lines.Length} lines, {fileInfo.Length} bytes) ---{Environment.NewLine}{content}";
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    [AIFunction<FileSystemContext>]
    [AIDescription("Writes content to a file, creating it if it doesn't exist or overwriting if it does.")]
    [RequiresPermission]
    public async Task<string> WriteFile(
        [AIDescription("The absolute path to the file to write")] string filePath,
        [AIDescription("The content to write to the file")] string content)
    {
        // Validate path
        if (string.IsNullOrWhiteSpace(filePath))
            return "Error: File path cannot be empty.";

        if (!Path.IsPathRooted(filePath))
            return $"Error: Path must be absolute, but was relative: {filePath}";

        if (!_context.IsPathWithinWorkspace(filePath))
            return $"Error: File path must be within workspace: {_context.WorkspaceRoot}";

        try
        {
            var fileExists = File.Exists(filePath);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Write file
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);

            var fileInfo = new FileInfo(filePath);
            var action = fileExists ? "Updated" : "Created";
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;

            return $"{action} file: {filePath} ({lines} lines, {fileInfo.Length} bytes)";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    [AIFunction<FileSystemContext>]
    [AIDescription("Lists the contents of a directory, showing files and subdirectories.")]
    public Task<string> ListDirectory(
        [AIDescription("The absolute path to the directory to list")] string directoryPath,
        [AIDescription("Whether to include subdirectories recursively")] bool recursive = false)
    {
        // Validate path
        if (string.IsNullOrWhiteSpace(directoryPath))
            return Task.FromResult("Error: Directory path cannot be empty.");

        if (!Path.IsPathRooted(directoryPath))
            return Task.FromResult($"Error: Path must be absolute, but was relative: {directoryPath}");

        if (!_context.IsPathWithinWorkspace(directoryPath))
            return Task.FromResult($"Error: Directory path must be within workspace: {_context.WorkspaceRoot}");

        if (!Directory.Exists(directoryPath))
            return Task.FromResult($"Error: Directory not found: {directoryPath}");

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- Directory: {directoryPath} ---");

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var directories = Directory.GetDirectories(directoryPath, "*", searchOption)
                .Select(d => new DirectoryInfo(d))
                .OrderBy(d => d.Name);

            var files = Directory.GetFiles(directoryPath, "*", searchOption)
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name);

            // List directories
            foreach (var dir in directories)
            {
                var relativePath = Path.GetRelativePath(directoryPath, dir.FullName);
                sb.AppendLine($"[DIR]  {relativePath}/");
            }

            // List files
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(directoryPath, file.FullName);
                var size = FormatFileSize(file.Length);
                sb.AppendLine($"[FILE] {relativePath} ({size})");
            }

            var totalDirs = directories.Count();
            var totalFiles = files.Count();
            sb.AppendLine();
            sb.AppendLine($"Total: {totalDirs} directories, {totalFiles} files");

            return Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error listing directory: {ex.Message}");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Detects the encoding of a file using Ude library
    /// </summary>
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

    /// <summary>
    /// Formats file size in human-readable format
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion
}

using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

public partial class CodingToolHarness
{
    private const int DefaultLineLimit = 2000;
    private const int MaxLineLimit = 2000;
    private const int MaxLineLength = 2000;
    private const long MaxTextReadBytes = 256 * 1024;

    /// <summary>
    /// Reads a text file as a bounded, line-numbered XML fragment.
    /// </summary>
    [AIFunction]
    [RequiresPermission]
    [Description("Reads a text file with line numbers. Use offset and limit for targeted reads when you know the relevant area. Avoid reading whole large files when compiler, grep, or test output already points to specific lines. To read lines 50-80, use offset: 50 and limit: 31. Avoid tiny repeated slices like 30-line chunks. If more context is needed, read a larger window.")]
    public async Task<object> ReadFile(
        [Description("The file path to read. Relative paths are resolved from the current working directory.")] string path,
        FunctionExecutionContext context,
        [Description("The 1-based line number to start reading from.")] int offset = 1,
        [Description("The maximum number of lines to return. Maximum: 2000.")] int limit = DefaultLineLimit)
    {
        try
        {
            var argumentError = ValidateReadArguments(path, offset, limit);
            if (argumentError != null)
                return FormatError(path ?? string.Empty, argumentError);

            if (Path.IsPathRooted(path) && IsBlockedDevicePath(Path.GetFullPath(path)))
                return FormatError(path, "Cannot read blocked device path.");

            var resolvedPath = ResolveReadPath(path, context);
            if (IsBlockedDevicePath(resolvedPath.FullPath))
                return FormatError(resolvedPath.FullPath, "Cannot read blocked device path.");

            var readFileStateKey = typeof(ReadFileState).FullName!;
            var priorSnapshot = context
                .Analyze(s => s.MiddlewareState.GetState<ReadFileState>(readFileStateKey))
                ?.FilesByPath.GetValueOrDefault(resolvedPath.FullPath);

            var sourceResult = await TryReadFromTextSourcesAsync(resolvedPath.FullPath, CancellationToken.None).ConfigureAwait(false);
            ReadFileTextResult result;

            if (sourceResult != null)
            {
                using var reader = sourceResult.Reader;
                result = await ReadTextRangeAsync(
                    sourceResult.FullPath,
                    reader,
                    offset,
                    limit,
                    sourceResult.LastWriteTimeUtc,
                    sourceResult.Length,
                    ReadFileSourceKind.TextSource,
                    sourceResult.Version).ConfigureAwait(false);
            }
            else
            {
                if (Directory.Exists(resolvedPath.FullPath))
                    return FormatError(resolvedPath.FullPath, "Path is a directory. Use ListDirectory instead.");

                if (!File.Exists(resolvedPath.FullPath))
                    return FormatError(resolvedPath.FullPath, BuildMissingFileMessage(resolvedPath.FullPath));

                var fileInfo = new FileInfo(resolvedPath.FullPath);
                var sample = await ReadByteSampleAsync(resolvedPath.FullPath).ConfigureAwait(false);
                var bomEncoding = DetectBomEncoding(sample);

                if (LooksBinary(sample, bomEncoding != null))
                    return FormatError(resolvedPath.FullPath, "Cannot read binary file.");

                var encoding = DetectTextEncoding(sample, bomEncoding);
                await using var stream = new FileStream(resolvedPath.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
                using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);

                result = await ReadTextRangeAsync(
                    resolvedPath.FullPath,
                    reader,
                    offset,
                    limit,
                    fileInfo.LastWriteTimeUtc,
                    fileInfo.Length,
                    ReadFileSourceKind.FileSystem,
                    null).ConfigureAwait(false);
            }

            if (CanReturnUnchanged(priorSnapshot, result, offset, limit))
                return FormatFileUnchanged(priorSnapshot!);

            var snapshot = new ReadFileSnapshot
            {
                Path = result.Path,
                ReadAt = DateTimeOffset.UtcNow,
                LastWriteTimeUtc = result.LastWriteTimeUtc,
                Length = result.Length,
                Offset = offset,
                Limit = limit,
                StartLine = result.Lines.Count == 0 ? 0 : result.StartLine,
                EndLine = result.EndLine,
                LinesRead = result.Lines.Count,
                TotalLines = result.TotalLines,
                Truncated = result.Truncated,
                Coverage = result.Coverage,
                SourceKind = result.SourceKind,
                SourceVersion = result.SourceVersion,
                ReturnedContentHash = result.ReturnedContentHash
            };

            context.ResultMetadata.Set(
                CodingToolMetadataKeys.ReadFileSnapshot,
                snapshot);

            return FormatReadResult(result);
        }
        catch (DecoderFallbackException)
        {
            return FormatError(path ?? string.Empty, "Unable to decode file as text.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return FormatError(path ?? string.Empty, $"Unable to read file: {ex.Message}");
        }
        catch (IOException ex)
        {
            return FormatError(path ?? string.Empty, $"Unable to read file: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FormatError(path ?? string.Empty, $"Unable to read file: {ex.Message}");
        }
    }

    private async ValueTask<ReadFileTextSourceResult?> TryReadFromTextSourcesAsync(string fullPath, CancellationToken cancellationToken)
    {
        foreach (var source in _readFileTextSources)
        {
            var result = await source.TryReadTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (result != null)
                return result;
        }

        return null;
    }

    private static string? ValidateReadArguments(string? path, int offset, int limit)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Path is required.";
        if (offset < 1)
            return "Offset must be greater than or equal to 1.";
        if (limit < 1 || limit > MaxLineLimit)
            return $"Limit must be between 1 and {MaxLineLimit.ToString(CultureInfo.InvariantCulture)}.";

        return null;
    }

    private static ResolvedReadPath ResolveReadPath(string path, FunctionExecutionContext context)
    {
        var trimmedPath = path.Trim();
        var fullPath = Path.GetFullPath(trimmedPath, Directory.GetCurrentDirectory());
        return new ResolvedReadPath(trimmedPath, fullPath);
    }

    private static bool IsBlockedDevicePath(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/');
        var blocked = new HashSet<string>(StringComparer.Ordinal)
        {
            "/dev/zero",
            "/dev/random",
            "/dev/urandom",
            "/dev/full",
            "/dev/stdin",
            "/dev/stdout",
            "/dev/stderr",
            "/dev/tty",
            "/dev/console",
            "/dev/fd/0",
            "/dev/fd/1",
            "/dev/fd/2"
        };

        if (blocked.Contains(normalized))
            return true;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 &&
               parts[0] == "proc" &&
               parts[2] == "fd" &&
               (parts[3] == "0" || parts[3] == "1" || parts[3] == "2");
    }

    private static async Task<ReadFileTextResult> ReadTextRangeAsync(
        string fullPath,
        TextReader reader,
        int offset,
        int limit,
        DateTimeOffset lastWriteTimeUtc,
        long length,
        ReadFileSourceKind sourceKind,
        string? sourceVersion)
    {
        var lines = new List<string>();
        var truncated = false;
        var contentTruncated = false;
        var linesWereShortened = false;
        var totalLines = 0;
        var outputBytes = 0L;
        var nextOffset = (int?)null;

        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            totalLines++;

            if (totalLines < offset)
                continue;

            if (lines.Count >= limit)
            {
                truncated = true;
                nextOffset ??= totalLines;
                continue;
            }

            var returnedLine = line;
            if (returnedLine.Length > MaxLineLength)
            {
                returnedLine = returnedLine[..MaxLineLength] + "... [line truncated]";
                truncated = true;
                contentTruncated = true;
                linesWereShortened = true;
            }

            var lineNumberedText = FormattableString.Invariant($"{totalLines}\t{returnedLine}\n");
            var lineBytes = Encoding.UTF8.GetByteCount(lineNumberedText);

            if (outputBytes + lineBytes > MaxTextReadBytes)
            {
                truncated = true;
                contentTruncated = true;
                nextOffset ??= totalLines;
                continue;
            }

            outputBytes += lineBytes;
            lines.Add(returnedLine);
        }

        var endLine = lines.Count == 0 ? 0 : offset + lines.Count - 1;
        var returnedContentHash = ComputeReturnedContentHash(lines);
        var coverage = DetermineCoverage(totalLines, lines.Count, offset, contentTruncated);
        return new ReadFileTextResult
        {
            Path = fullPath,
            Lines = lines,
            StartLine = offset,
            EndLine = endLine,
            TotalLines = totalLines,
            Truncated = truncated,
            LinesWereShortened = linesWereShortened,
            NextOffset = nextOffset,
            LastWriteTimeUtc = lastWriteTimeUtc,
            Length = length,
            Coverage = coverage,
            SourceKind = sourceKind,
            SourceVersion = sourceVersion,
            ReturnedContentHash = returnedContentHash
        };
    }

    private static bool CanReturnUnchanged(
        ReadFileSnapshot? snapshot,
        ReadFileTextResult result,
        int offset,
        int limit)
    {
        if (snapshot == null)
            return false;

        return snapshot.Path == result.Path &&
               snapshot.Offset == offset &&
               snapshot.Limit == limit &&
               snapshot.Length == result.Length &&
               snapshot.LastWriteTimeUtc == result.LastWriteTimeUtc &&
               snapshot.SourceKind == result.SourceKind &&
               string.Equals(snapshot.SourceVersion, result.SourceVersion, StringComparison.Ordinal) &&
               snapshot.ReturnedContentHash == result.ReturnedContentHash;
    }

    private static string ComputeReturnedContentHash(IReadOnlyList<string> lines)
    {
        var content = string.Join('\n', lines);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ReadFileCoverage DetermineCoverage(int totalLines, int linesRead, int offset, bool contentTruncated)
    {
        if (totalLines == 0)
            return ReadFileCoverage.EmptyFile;

        if (contentTruncated)
            return ReadFileCoverage.Truncated;

        return offset == 1 && linesRead == totalLines
            ? ReadFileCoverage.FullFile
            : ReadFileCoverage.PartialRange;
    }

    private static string FormatReadResult(ReadFileTextResult result)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("file");
        writer.WriteAttributeString("path", result.Path);
        writer.WriteAttributeString("start_line", result.StartLine.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("lines_read", result.Lines.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("total_lines", result.TotalLines.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("truncated", result.Truncated.ToString().ToLowerInvariant());
        writer.WriteAttributeString("coverage", FormatCoverage(result.Coverage));

        if (result.TotalLines == 0)
        {
            writer.WriteStartElement("empty_file");
            writer.WriteEndElement();
        }
        else if (result.Lines.Count == 0 && result.StartLine > result.TotalLines)
        {
            writer.WriteStartElement("no_content");
            writer.WriteAttributeString("reason", "offset_beyond_end");
            writer.WriteEndElement();
        }
        else
        {
            writer.WriteString("\n");
            writer.WriteString(BuildLineNumberedText(result.Lines, result.StartLine).TrimEnd('\n'));
            writer.WriteString("\n");
        }

        if (result.NextOffset.HasValue)
        {
            writer.WriteStartElement("next_read");
            writer.WriteAttributeString("offset", result.NextOffset.Value.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("limit", MaxLineLimit.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("reason", "output_truncated");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static string FormatCoverage(ReadFileCoverage coverage)
        => coverage switch
        {
            ReadFileCoverage.EmptyFile => "empty_file",
            ReadFileCoverage.FullFile => "full_file",
            ReadFileCoverage.PartialRange => "partial_range",
            ReadFileCoverage.Truncated => "truncated",
            _ => "unknown"
        };

    private static string FormatFileUnchanged(ReadFileSnapshot snapshot)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("file_unchanged");
        writer.WriteAttributeString("path", snapshot.Path);
        writer.WriteAttributeString("start_line", snapshot.Offset.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("limit", snapshot.Limit.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("last_read_at", snapshot.ReadAt.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("File unchanged since last read. Use the previous ReadFile result in context.");
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static string FormatError(string path, string message)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("error");
        writer.WriteAttributeString("tool", "ReadFile");
        if (!string.IsNullOrEmpty(path))
            writer.WriteAttributeString("path", path);
        writer.WriteString(message);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static string BuildLineNumberedText(IReadOnlyList<string> lines, int startLine)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
            builder.Append(CultureInfo.InvariantCulture, $"{startLine + i}\t{lines[i]}\n");

        return builder.ToString();
    }

    private static string BuildMissingFileMessage(string fullPath)
    {
        var suggestion = FindSimilarFile(fullPath);
        return suggestion == null
            ? "File does not exist."
            : $"File does not exist. Did you mean {suggestion}?";
    }

    private static string? FindSimilarFile(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        var requestedBaseName = Path.GetFileNameWithoutExtension(fullPath);
        if (string.IsNullOrEmpty(requestedBaseName))
            return null;

        try
        {
            return Directory.EnumerateFiles(directory)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .FirstOrDefault(name => string.Equals(
                    Path.GetFileNameWithoutExtension(name),
                    requestedBaseName,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}

namespace HPDOS.ToolHarnesses.Middleware
{
/// <summary>
/// Provides live text for a path before `ReadFile` falls back to disk.
/// </summary>
public interface IReadFileTextSource
{
    /// <summary>
    /// Returns a readable text snapshot for a resolved path, or null when the source does not own it.
    /// </summary>
    ValueTask<ReadFileTextSourceResult?> TryReadTextAsync(
        string fullPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// Text snapshot returned by an <see cref="IReadFileTextSource"/>.
/// </summary>
public sealed record ReadFileTextSourceResult
{
    /// <summary>
    /// Resolved absolute path represented by the snapshot.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Reader for the text content.
    /// </summary>
    public required TextReader Reader { get; init; }

    /// <summary>
    /// Last known write time or source version time.
    /// </summary>
    public required DateTimeOffset LastWriteTimeUtc { get; init; }

    /// <summary>
    /// Source content length in bytes or characters, depending on host metadata.
    /// </summary>
    public required long Length { get; init; }

    /// <summary>
    /// Optional host content type or language identifier.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Optional host version stamp.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// True when the snapshot includes unsaved editor content.
    /// </summary>
    public bool IsUnsavedEditorContent { get; init; }
}

/// <summary>
/// Session-scoped read tracking state for coding file tools.
/// </summary>
[MiddlewareState(Scope = StateScope.Session)]
public sealed record ReadFileState
{
    /// <summary>
    /// Last successful read snapshot for each resolved path.
    /// </summary>
    public IReadOnlyDictionary<string, ReadFileSnapshot> FilesByPath { get; init; }
        = new Dictionary<string, ReadFileSnapshot>(StringComparer.Ordinal);
}

public static class CodingToolMetadataKeys
{
    public const string ReadFileSnapshot = "coding.readFile.snapshot";
    public const string FileMutationSnapshot = "coding.fileMutation.snapshot";
    public const string DebugOperation = "coding.debug.operation";
    public const string DebugSessionSnapshot = "coding.debug.sessionSnapshot";
    public const string DebugStopSnapshot = "coding.debug.stopSnapshot";
    public const string DebugBreakpoints = "coding.debug.breakpoints";
    public const string DebugStackFrames = "coding.debug.stackFrames";
    public const string DebugCapabilities = "coding.debug.capabilities";
    public const string DebugOutputReference = "coding.debug.outputReference";
    public const string DebugAdapterDiagnosticReference = "coding.debug.adapterDiagnosticReference";
}

/// <summary>
/// Metadata for a successful `ReadFile` result.
/// </summary>
public sealed record ReadFileSnapshot
{
    /// <summary>
    /// Resolved absolute path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Time this read was emitted to the model.
    /// </summary>
    public DateTimeOffset ReadAt { get; init; }

    /// <summary>
    /// File or source last-write timestamp at read time.
    /// </summary>
    public DateTimeOffset LastWriteTimeUtc { get; init; }

    /// <summary>
    /// File or source length at read time.
    /// </summary>
    public long Length { get; init; }

    /// <summary>
    /// 1-based line offset used by the read.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Maximum line count requested by the read.
    /// </summary>
    public int Limit { get; init; }

    /// <summary>
    /// First returned line number, or 0 when no content was returned.
    /// </summary>
    public int StartLine { get; init; }

    /// <summary>
    /// Last returned line number, or 0 when no content was returned.
    /// </summary>
    public int EndLine { get; init; }

    /// <summary>
    /// Number of lines returned.
    /// </summary>
    public int LinesRead { get; init; }

    /// <summary>
    /// Total line count observed while streaming the file.
    /// </summary>
    public int TotalLines { get; init; }

    /// <summary>
    /// Whether the emitted result was truncated.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// How much of the file the read covered.
    /// </summary>
    public ReadFileCoverage Coverage { get; init; }

    /// <summary>
    /// The source that supplied the text.
    /// </summary>
    public ReadFileSourceKind SourceKind { get; init; }

    /// <summary>
    /// Optional version stamp supplied by a text source.
    /// </summary>
    public string? SourceVersion { get; init; }

    /// <summary>
    /// SHA-256 hash of returned file text without line-number prefixes.
    /// </summary>
    public string? ReturnedContentHash { get; init; }
}

/// <summary>
/// Describes how much of a file was returned by `ReadFile`.
/// </summary>
public enum ReadFileCoverage
{
    EmptyFile,
    FullFile,
    PartialRange,
    Truncated
}

/// <summary>
/// Identifies where `ReadFile` obtained the text.
/// </summary>
public enum ReadFileSourceKind
{
    FileSystem,
    TextSource
}

internal sealed record ResolvedReadPath(string InputPath, string FullPath);

internal sealed record ReadFileTextResult
{
    public required string Path { get; init; }

    public required IReadOnlyList<string> Lines { get; init; }

    public int StartLine { get; init; }

    public int EndLine { get; init; }

    public int TotalLines { get; init; }

    public bool Truncated { get; init; }

    public bool LinesWereShortened { get; init; }

    public int? NextOffset { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public long Length { get; init; }

    public ReadFileCoverage Coverage { get; init; }

    public ReadFileSourceKind SourceKind { get; init; }

    public string? SourceVersion { get; init; }

    public string ReturnedContentHash { get; init; } = string.Empty;
}
}

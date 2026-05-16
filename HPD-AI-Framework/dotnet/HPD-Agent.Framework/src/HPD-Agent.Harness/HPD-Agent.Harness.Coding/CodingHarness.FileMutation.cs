using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPDOS.Harneses.Middleware;

public partial class CodingHarness
{
    private const long MaxMutableFileBytes = 50 * 1024 * 1024;
    private const int MaxEventSnapshotChars = 500_000;
    private const int MaxEventTextEditChars = 100_000;
    private const int MaxEventHunks = 200;
    private const int MaxMutationEventNotes = 50;

    private static ResolvedMutationPath ResolveMutationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new FileMutationException(FileMutationErrorKind.InvalidArguments, "Path is required.");

        var trimmedPath = path.Trim();
        var fullPath = Path.GetFullPath(trimmedPath, Directory.GetCurrentDirectory());
        return new ResolvedMutationPath(trimmedPath, fullPath);
    }

    private static bool IsBlockedMutationPath(string fullPath)
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

    private static bool IsWindowsUncPath(string path)
        => path.StartsWith(@"\\", StringComparison.Ordinal) ||
           path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsNotebookPath(string fullPath)
        => string.Equals(Path.GetExtension(fullPath), ".ipynb", StringComparison.OrdinalIgnoreCase);

    private static void ValidateMutationPath(ResolvedMutationPath path)
    {
        if (IsWindowsUncPath(path.InputPath) || IsWindowsUncPath(path.FullPath))
            throw new FileMutationException(FileMutationErrorKind.WindowsUncPath, "Cannot mutate a Windows UNC path without host approval.");

        if (IsBlockedMutationPath(path.FullPath))
            throw new FileMutationException(FileMutationErrorKind.BlockedDevicePath, "Cannot mutate blocked device path.");

        if (IsNotebookPath(path.FullPath))
            throw new FileMutationException(FileMutationErrorKind.NotebookFile, "File is a notebook. Use NotebookEdit when available.");
    }

    private static string? FindSimilarMutationPath(string fullPath)
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

    private static async Task<FileMutationContent> ReadMutationContentAsync(string fullPath, bool allowMissing, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(fullPath))
            throw new FileMutationException(FileMutationErrorKind.PathIsDirectory, "Cannot mutate a directory.");

        if (!File.Exists(fullPath))
        {
            if (!allowMissing)
                throw new FileMutationException(FileMutationErrorKind.FileNotFound, BuildMissingMutationMessage(fullPath));

            return FileMutationContent.Missing(fullPath);
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > MaxMutableFileBytes)
            throw new FileMutationException(FileMutationErrorKind.FileTooLarge, "File is too large to mutate.");

        var sample = await ReadByteSampleAsync(fullPath).ConfigureAwait(false);
        var bomEncoding = DetectBomEncoding(sample);
        if (LooksBinary(sample, bomEncoding != null))
            throw new FileMutationException(FileMutationErrorKind.BinaryFile, "Cannot mutate binary file.");

        var encoding = DetectTextEncoding(fullPath, sample, bomEncoding);
        string text;
        try
        {
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);
            text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DecoderFallbackException exception)
        {
            throw new FileMutationException(FileMutationErrorKind.DecodeError, $"Cannot decode text file: {exception.Message}");
        }

        return new FileMutationContent(
            Text: text,
            Exists: true,
            Encoding: encoding,
            HasBom: bomEncoding != null && encoding.GetPreamble().Length > 0,
            LineEnding: DetectLineEnding(text),
            LastWriteTimeUtc: fileInfo.LastWriteTimeUtc,
            ByteLength: fileInfo.Length,
            ContentHash: ComputeContentHash(text));
    }

    private static FileMutationLineEnding DetectLineEnding(string text)
    {
        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;

            if (i > 0 && text[i - 1] == '\r')
                crlf++;
            else
                lf++;
        }

        if (crlf == 0 && lf == 0)
            return FileMutationLineEnding.Unknown;
        if (crlf > 0 && lf > 0)
            return FileMutationLineEnding.Mixed;
        return crlf > 0 ? FileMutationLineEnding.Crlf : FileMutationLineEnding.Lf;
    }

    private static string NormalizeLineEndings(string text, FileMutationLineEnding lineEnding)
    {
        if (lineEnding is FileMutationLineEnding.Mixed or FileMutationLineEnding.Unknown)
            return text;

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return lineEnding == FileMutationLineEnding.Crlf
            ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : normalized;
    }

    private static string ComputeContentHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static long ComputeEncodedByteLength(string text, Encoding encoding)
        => encoding.GetPreamble().LongLength + encoding.GetByteCount(text);

    private static IReadOnlyList<string> SplitMutationLines(string text)
    {
        using var reader = new StringReader(text);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
            lines.Add(line);

        return lines;
    }

    private static FileMutationRange GetRange(string text, int startOffset, int length)
    {
        var start = GetLineColumn(text, startOffset);
        var end = GetLineColumn(text, Math.Min(text.Length, startOffset + length));
        return new FileMutationRange(
            start.Line,
            start.Column,
            end.Line,
            end.Column,
            startOffset,
            length);
    }

    private static (int? FirstLine, int? LastLine) GetChangedLineRange(string before, string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return (null, null);

        var beforeLines = SplitMutationLines(before);
        var afterLines = SplitMutationLines(after);
        var min = Math.Min(beforeLines.Count, afterLines.Count);
        var first = 0;
        while (first < min && string.Equals(beforeLines[first], afterLines[first], StringComparison.Ordinal))
            first++;

        var beforeLast = beforeLines.Count - 1;
        var afterLast = afterLines.Count - 1;
        while (beforeLast >= first &&
               afterLast >= first &&
               string.Equals(beforeLines[beforeLast], afterLines[afterLast], StringComparison.Ordinal))
        {
            beforeLast--;
            afterLast--;
        }

        return (first + 1, Math.Max(beforeLast, afterLast) + 1);
    }

    private static FileMutationDiffStat BuildDiffStat(string before, string after)
    {
        var diff = new Differ().CreateLineDiffs(before, after, ignoreWhitespace: false);
        var added = 0;
        var removed = 0;
        foreach (var block in diff.DiffBlocks)
        {
            removed += block.DeleteCountA;
            added += block.InsertCountB;
        }

        return new FileMutationDiffStat(
            added,
            removed,
            Math.Max(0, after.Length - before.Length),
            Math.Max(0, before.Length - after.Length));
    }

    private static IReadOnlyList<FileMutationHunk> BuildDiffHunks(string before, string after, out bool truncated)
    {
        truncated = false;
        var diff = InlineDiffBuilder.Diff(before, after);
        var lines = new List<string>();
        foreach (var line in diff.Lines)
        {
            if (lines.Count >= MaxEventHunks)
            {
                truncated = true;
                break;
            }

            var prefix = line.Type switch
            {
                ChangeType.Inserted => "+",
                ChangeType.Deleted => "-",
                _ => " "
            };
            lines.Add(prefix + line.Text);
        }

        return
        [
            new FileMutationHunk(
                1,
                SplitMutationLines(before).Count,
                1,
                SplitMutationLines(after).Count,
                lines)
        ];
    }

    internal async Task<FileMutationResult> ApplyTextMutationAsync(FileMutationRequest request, CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveMutationPath(request.Path);
        ValidateMutationPath(resolvedPath);

        await using var mutationLock = await _fileMutationLockProvider
            .AcquireAsync(resolvedPath.FullPath, cancellationToken)
            .ConfigureAwait(false);

        var before = await ReadMutationContentAsync(resolvedPath.FullPath, request.AllowCreate, cancellationToken).ConfigureAwait(false);
        request.ValidateBeforeMutation?.Invoke(before);

        var created = !before.Exists;
        var selectedEncoding = before.Exists ? before.Encoding : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var updatedText = request.NormalizeLineEndingsToExistingFile
            ? NormalizeLineEndings(request.UpdatedText, before.LineEnding)
            : request.UpdatedText;
        var changed = !before.Exists || !string.Equals(before.Text, updatedText, StringComparison.Ordinal);
        var finalText = updatedText;
        var finalByteLength = ComputeEncodedByteLength(finalText, selectedEncoding);
        var finalHash = ComputeContentHash(finalText);
        var finalLastWrite = before.LastWriteTimeUtc ?? DateTimeOffset.UtcNow;
        var notes = request.Notes.Take(MaxMutationEventNotes).ToList();
        var textEdits = NormalizeMutationTextEdits(request.TextEdits);

        if (changed)
        {
            await CaptureMutationHistoryAsync(request.ToolName, resolvedPath.FullPath, before, notes, cancellationToken).ConfigureAwait(false);
            var sinkResult = await TryMutateThroughSinkAsync(
                request,
                resolvedPath.FullPath,
                before,
                finalText,
                selectedEncoding,
                textEdits,
                cancellationToken).ConfigureAwait(false);

            if (sinkResult != null)
            {
                finalText = sinkResult.FinalText;
                finalByteLength = sinkResult.ByteLength ?? ComputeEncodedByteLength(finalText, selectedEncoding);
                finalHash = sinkResult.ContentHash ?? ComputeContentHash(finalText);
                finalLastWrite = sinkResult.LastWriteTimeUtc ?? DateTimeOffset.UtcNow;
            }
            else
            {
                if (created && request.CreateParentDirectories)
                    Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath.FullPath) ?? Directory.GetCurrentDirectory());

                finalLastWrite = await WriteTextAtomicallyAsync(
                    resolvedPath.FullPath,
                    finalText,
                    selectedEncoding,
                    before.Exists,
                    request.CreateParentDirectories,
                    cancellationToken).ConfigureAwait(false);

                var finalInfo = new FileInfo(resolvedPath.FullPath);
                finalByteLength = finalInfo.Length;
                finalHash = ComputeContentHash(finalText);
            }
        }

        var changedLines = GetChangedLineRange(before.Text, finalText);
        var eventEmitted = false;
        if (changed)
        {
            SetFileMutationMetadata(
                request.FunctionContext,
                request.ToolName,
                resolvedPath.FullPath,
                request.Kind,
                finalText.Length <= MaxEventSnapshotChars ? finalText : null,
                finalByteLength,
                finalLastWrite);

            if (request.EventFactory != null)
            {
                var mutationEvent = BuildFileMutationEvent(
                    request with
                    {
                        Path = resolvedPath.FullPath,
                        TextEdits = textEdits,
                        Notes = notes
                    },
                    before,
                    finalText,
                    finalByteLength,
                    finalHash,
                    finalLastWrite,
                    created,
                    changed);
                eventEmitted = EmitFileMutationEvent(request.FunctionContext, mutationEvent);
            }
        }

        return new FileMutationResult(
            resolvedPath.FullPath,
            created,
            changed,
            before.Text,
            finalText,
            finalByteLength,
            finalHash,
            finalLastWrite,
            changedLines.FirstLine,
            changedLines.LastLine,
            textEdits,
            notes,
            eventEmitted);
    }

    private async Task CaptureMutationHistoryAsync(
        string toolName,
        string fullPath,
        FileMutationContent before,
        ICollection<FileMutationNote> notes,
        CancellationToken cancellationToken)
    {
        if (_fileMutationHistorySinks.Count == 0 || !before.Exists)
            return;

        var request = new FileMutationHistoryRequest
        {
            ToolName = toolName,
            Path = fullPath,
            BeforeText = before.Text,
            ContentHash = before.ContentHash,
            ByteLength = before.ByteLength
        };

        foreach (var sink in _fileMutationHistorySinks)
        {
            try
            {
                await sink.CaptureBeforeMutationAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (notes.Count < MaxMutationEventNotes)
                    notes.Add(new FileMutationNote(null, "history_capture_failed"));
            }
        }
    }

    private async Task<FileMutationSinkResult?> TryMutateThroughSinkAsync(
        FileMutationRequest request,
        string fullPath,
        FileMutationContent before,
        string updatedText,
        Encoding selectedEncoding,
        IReadOnlyList<FileMutationTextEdit> textEdits,
        CancellationToken cancellationToken)
    {
        if (_fileMutationTextSinks.Count == 0)
            return null;

        var sinkRequest = new FileMutationSinkRequest
        {
            ToolName = request.ToolName,
            Path = fullPath,
            BeforeText = before.Exists ? before.Text : null,
            AfterText = updatedText,
            Kind = request.Kind,
            TextEdits = textEdits,
            EncodingName = selectedEncoding.WebName,
            LineEnding = FormatEnum(DetectLineEnding(updatedText))
        };

        foreach (var sink in _fileMutationTextSinks)
        {
            try
            {
                var result = await sink.TryMutateTextAsync(sinkRequest, cancellationToken).ConfigureAwait(false);
                if (result != null)
                    return result;
            }
            catch (Exception exception)
            {
                throw new FileMutationException(FileMutationErrorKind.HostSinkFailed, $"Host text sink failed: {exception.Message}");
            }
        }

        return null;
    }

    private static IReadOnlyList<FileMutationTextEdit> NormalizeMutationTextEdits(IReadOnlyList<FileMutationTextEdit> textEdits)
    {
        if (textEdits.Count == 0)
            return textEdits;

        var normalized = new List<FileMutationTextEdit>(textEdits.Count);
        foreach (var edit in textEdits)
        {
            var payloadLength = (edit.OldText?.Length ?? 0) + (edit.NewText?.Length ?? 0);
            normalized.Add(payloadLength > MaxEventTextEditChars
                ? edit with
                {
                    OldText = null,
                    NewText = null,
                    TextOmitted = true,
                    OmissionReason = "text_edit_too_large"
                }
                : edit);
        }

        return normalized;
    }

    private static async Task<DateTimeOffset> WriteTextAtomicallyAsync(
        string fullPath,
        string text,
        Encoding encoding,
        bool replacingExistingFile,
        bool createParentDirectories,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
            directory = Directory.GetCurrentDirectory();

        if (!Directory.Exists(directory))
        {
            if (!createParentDirectories)
                throw new FileMutationException(FileMutationErrorKind.FileNotFound, "Parent directory does not exist.");

            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, text, encoding, cancellationToken).ConfigureAwait(false);
            if (!replacingExistingFile)
            {
                File.Move(tempPath, fullPath);
                return new FileInfo(fullPath).LastWriteTimeUtc;
            }

            try
            {
                File.Replace(tempPath, fullPath, destinationBackupFileName: null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch (IOException)
            {
                File.Move(tempPath, fullPath, overwrite: true);
            }

            return new FileInfo(fullPath).LastWriteTimeUtc;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static void SetFileMutationMetadata(
        FunctionExecutionContext context,
        string toolName,
        string path,
        CodingFileMutationKind kind,
        string? text,
        long byteLength,
        DateTimeOffset lastWriteTimeUtc)
    {
        context.ResultMetadata.Set(
            CodingToolMetadataKeys.FileMutationSnapshot,
            new CodingFileMutationSnapshot
            {
                ToolName = toolName,
                Path = path,
                Kind = kind,
                Text = text,
                ByteLength = byteLength,
                LastWriteTimeUtc = lastWriteTimeUtc
            });
    }

    private static bool EmitFileMutationEvent(
        FunctionExecutionContext context,
        FileMutationAppliedEvent mutationEvent)
    {
        try
        {
            return context.TryEmit(mutationEvent);
        }
        catch
        {
            return false;
        }
    }

    private static string FormatMutationError(string toolName, FileMutationErrorKind kind, string? path, string message)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingHarnessXmlWriter(builder);
        writer.WriteStartElement("error");
        writer.WriteAttributeString("tool", toolName);
        writer.WriteAttributeString("kind", FormatEnum(kind));
        if (!string.IsNullOrWhiteSpace(path))
            writer.WriteAttributeString("path", path);
        writer.WriteString(message);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static FileMutationAppliedEvent BuildFileMutationEvent(
        FileMutationRequest request,
        FileMutationContent before,
        string updatedText,
        long finalByteLength,
        string finalHash,
        DateTimeOffset finalLastWrite,
        bool created,
        bool changed)
    {
        var context = request.FunctionContext;
        var beforeSnapshot = BuildMutationSnapshot(before);
        var afterSnapshot = BuildMutationSnapshot(
            updatedText,
            finalHash,
            finalByteLength,
            SplitMutationLines(updatedText).Count,
            before.Exists ? before.Encoding.WebName : "utf-8",
            before.Exists && before.HasBom,
            DetectLineEnding(updatedText),
            finalLastWrite);
        var hunks = BuildDiffHunks(before.Text, updatedText, out var hunksTruncated);
        var diffStat = BuildDiffStat(before.Text, updatedText);

        return request.EventFactory!(new FileMutationEventBuildRequest(
            ToolCallId: context.FunctionCallId,
            FunctionName: context.FunctionName,
            Path: request.Path,
            DisplayPath: request.Path,
            MutationKind: request.Kind,
            Created: created,
            Changed: changed,
            Before: beforeSnapshot,
            After: afterSnapshot,
            TextEdits: request.TextEdits,
            Hunks: hunks,
            HunksTruncated: hunksTruncated,
            DiffStat: diffStat,
            Notes: request.Notes));
    }

    private static FileMutationSnapshot BuildMutationSnapshot(FileMutationContent content)
        => BuildMutationSnapshot(
            content.Text,
            content.ContentHash,
            content.ByteLength,
            SplitMutationLines(content.Text).Count,
            content.Encoding.WebName,
            content.HasBom,
            content.LineEnding,
            content.LastWriteTimeUtc);

    private static FileMutationSnapshot BuildMutationSnapshot(
        string text,
        string contentHash,
        long byteLength,
        int lineCount,
        string encodingName,
        bool hasBom,
        FileMutationLineEnding lineEnding,
        DateTimeOffset? lastWriteTimeUtc)
    {
        var omitText = text.Length > MaxEventSnapshotChars;
        return new FileMutationSnapshot(
            omitText ? null : text,
            contentHash,
            byteLength,
            lineCount,
            encodingName,
            hasBom,
            FormatEnum(lineEnding),
            lastWriteTimeUtc,
            omitText,
            omitText ? "snapshot_too_large" : null);
    }

    private static string BuildMissingMutationMessage(string fullPath)
    {
        var suggestion = FindSimilarMutationPath(fullPath);
        return suggestion == null
            ? "File does not exist."
            : $"File does not exist. Did you mean {suggestion}?";
    }

    private static (int Line, int Column) GetLineColumn(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return (line, column);
    }
}

internal sealed record ResolvedMutationPath(string InputPath, string FullPath);

internal sealed record FileMutationContent(
    string Text,
    bool Exists,
    Encoding Encoding,
    bool HasBom,
    FileMutationLineEnding LineEnding,
    DateTimeOffset? LastWriteTimeUtc,
    long ByteLength,
    string ContentHash)
{
    public static FileMutationContent Missing(string path)
        => new(
            string.Empty,
            false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            false,
            FileMutationLineEnding.Unknown,
            null,
            0,
            "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
}

internal sealed record FileMutationRequest(
    string ToolName,
    string Path,
    string UpdatedText,
    CodingFileMutationKind Kind,
    bool AllowCreate,
    bool CreateParentDirectories,
    bool NormalizeLineEndingsToExistingFile,
    IReadOnlyList<FileMutationTextEdit> TextEdits,
    IReadOnlyList<FileMutationNote> Notes,
    FunctionExecutionContext FunctionContext,
    Action<FileMutationContent>? ValidateBeforeMutation = null,
    FileMutationEventFactory? EventFactory = null);

internal delegate FileMutationAppliedEvent FileMutationEventFactory(FileMutationEventBuildRequest request);

internal sealed record FileMutationEventBuildRequest(
    string ToolCallId,
    string FunctionName,
    string Path,
    string DisplayPath,
    CodingFileMutationKind MutationKind,
    bool Created,
    bool Changed,
    FileMutationSnapshot Before,
    FileMutationSnapshot After,
    IReadOnlyList<FileMutationTextEdit> TextEdits,
    IReadOnlyList<FileMutationHunk> Hunks,
    bool HunksTruncated,
    FileMutationDiffStat DiffStat,
    IReadOnlyList<FileMutationNote> Notes);

internal sealed record FileMutationResult(
    string Path,
    bool Created,
    bool Changed,
    string OriginalText,
    string UpdatedText,
    long ByteLength,
    string ContentHash,
    DateTimeOffset LastWriteTimeUtc,
    int? FirstChangedLine,
    int? LastChangedLine,
    IReadOnlyList<FileMutationTextEdit> TextEdits,
    IReadOnlyList<FileMutationNote> Notes,
    bool EventEmitted);

internal enum FileMutationLineEnding
{
    Lf,
    Crlf,
    Mixed,
    Unknown
}

internal enum FileMutationErrorKind
{
    InvalidArguments,
    FileNotFound,
    PathIsDirectory,
    BlockedDevicePath,
    WindowsUncPath,
    BinaryFile,
    NotebookFile,
    FileTooLarge,
    DecodeError,
    StaleFile,
    WriteFailed,
    HistoryCaptureFailed,
    HostSinkFailed
}

internal sealed class FileMutationException : Exception
{
    public FileMutationException(FileMutationErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public FileMutationErrorKind Kind { get; }
}

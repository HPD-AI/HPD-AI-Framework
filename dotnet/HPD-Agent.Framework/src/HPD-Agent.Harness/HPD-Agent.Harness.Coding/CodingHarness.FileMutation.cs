using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using HPD.Agent;
using HPD.Agent.Sandbox;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;

public partial class CodingToolHarness
{
    private const long MaxMutableFileBytes = 50 * 1024 * 1024;
    private const int MaxEventSnapshotChars = 500_000;
    private const int MaxEventTextEditChars = 100_000;
    private const int MaxEventHunks = 200;
    private const int FileMutationDiffContextLines = 3;
    private const int MaxMutationEventNotes = 50;

    private static ResolvedMutationPath ResolveMutationPath(string path, FunctionExecutionContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new FileMutationException(FileMutationErrorKind.InvalidArguments, "Path is required.");

        var trimmedPath = path.Trim();
        var fullPath = context?.RunConfig.Context?.Properties?.ContainsKey(AgentWorkspace.ContextKey) == true
            ? AgentWorkspace.From(context.RunConfig).CanonicalizeExplicitPath(trimmedPath)
            : Path.GetFullPath(trimmedPath, Directory.GetCurrentDirectory());
        return new ResolvedMutationPath(trimmedPath, fullPath);
    }

    private static bool IsBlockedMutationPath(string fullPath)
    {
        if (OperatingSystem.IsWindows() && HasWindowsDeviceName(fullPath))
            return true;

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

    private static bool HasWindowsDeviceName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var separator = trimmed.LastIndexOfAny(['\\', '/']);
        var leaf = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        var extension = leaf.IndexOf('.');
        var name = extension >= 0 ? leaf[..extension] : leaf;
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase) ||
            (name.Length == 4 &&
                (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                name[3] is >= '1' and <= '9');
    }

    private static bool IsWindowsUncPath(string path)
        => path.StartsWith(@"\\", StringComparison.Ordinal) ||
           path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsNotebookPath(string fullPath)
        => string.Equals(Path.GetExtension(fullPath), ".ipynb", StringComparison.OrdinalIgnoreCase);

    private static void ValidateMutationPath(ResolvedMutationPath path)
    {
        if (IsBlockedMutationPath(path.FullPath))
            throw new FileMutationException(FileMutationErrorKind.BlockedDevicePath, "Cannot mutate blocked device path.");

        if (IsWindowsUncPath(path.InputPath) || IsWindowsUncPath(path.FullPath))
            throw new FileMutationException(FileMutationErrorKind.WindowsUncPath, "Cannot mutate a Windows UNC path without host approval.");

        if (IsNotebookPath(path.FullPath))
            throw new FileMutationException(FileMutationErrorKind.NotebookFile, "File is a notebook. Use NotebookEdit when available.");
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

        var encoding = DetectTextEncoding(sample, bomEncoding);
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
        var lines = new List<FileMutationDiffLine>();
        var oldLine = 1;
        var newLine = 1;
        foreach (var line in diff.Lines)
        {
            var entry = line.Type switch
            {
                ChangeType.Inserted => new FileMutationDiffLine(
                    '+',
                    line.Text,
                    OldLine: null,
                    NewLine: newLine++),
                ChangeType.Deleted => new FileMutationDiffLine(
                    '-',
                    line.Text,
                    OldLine: oldLine++,
                    NewLine: null),
                _ => new FileMutationDiffLine(
                    ' ',
                    line.Text,
                    OldLine: oldLine++,
                    NewLine: newLine++)
            };

            lines.Add(entry);
        }

        var windows = BuildDiffWindows(lines, FileMutationDiffContextLines);
        if (windows.Count == 0 && lines.Count > 0)
        {
            windows.Add(new FileMutationDiffWindow(0, Math.Min(lines.Count - 1, MaxEventHunks - 1)));
        }

        var hunks = new List<FileMutationHunk>();
        var emittedLines = 0;
        foreach (var window in windows)
        {
            if (emittedLines >= MaxEventHunks)
            {
                truncated = true;
                break;
            }

            var windowLines = lines
                .Skip(window.Start)
                .Take(window.End - window.Start + 1)
                .ToList();
            var remaining = MaxEventHunks - emittedLines;
            if (windowLines.Count > remaining)
            {
                windowLines = windowLines.Take(remaining).ToList();
                truncated = true;
            }

            if (windowLines.Count == 0)
            {
                continue;
            }

            hunks.Add(new FileMutationHunk(
                GetHunkOldStart(windowLines),
                windowLines.Count(static line => line.OldLine.HasValue),
                GetHunkNewStart(windowLines),
                windowLines.Count(static line => line.NewLine.HasValue),
                windowLines.Select(static line => $"{line.Sign}{line.Text}").ToArray()));
            emittedLines += windowLines.Count;

            if (truncated)
            {
                break;
            }
        }

        return hunks;
    }

    private static List<FileMutationDiffWindow> BuildDiffWindows(
        IReadOnlyList<FileMutationDiffLine> lines,
        int contextLines)
    {
        var windows = new List<FileMutationDiffWindow>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Sign is not ('+' or '-'))
            {
                continue;
            }

            var start = Math.Max(0, i - contextLines);
            var end = Math.Min(lines.Count - 1, i + contextLines);
            if (windows.Count > 0 && start <= windows[^1].End + 1)
            {
                windows[^1] = windows[^1] with { End = Math.Max(windows[^1].End, end) };
                continue;
            }

            windows.Add(new FileMutationDiffWindow(start, end));
        }

        return windows;
    }

    private static int GetHunkOldStart(IReadOnlyList<FileMutationDiffLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.OldLine.HasValue)
            {
                return line.OldLine.Value;
            }
        }

        var insertedLine = 1;
        foreach (var line in lines)
        {
            if (line.NewLine.HasValue)
            {
                insertedLine = line.NewLine.Value;
                break;
            }
        }

        return Math.Max(0, insertedLine - 1);
    }

    private static int GetHunkNewStart(IReadOnlyList<FileMutationDiffLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.NewLine.HasValue)
            {
                return line.NewLine.Value;
            }
        }

        return 1;
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
                eventEmitted = await EmitFileMutationEventAsync(
                    request.FunctionContext,
                    mutationEvent,
                    cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Applies a text mutation through the session workspace file store. The
    /// expected content version came from the same read used for tool policy,
    /// so the provider rejects a concurrent process or external mutation.
    /// </summary>
    internal async Task<FileMutationResult> ApplyWorkspaceTextMutationAsync(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath path,
        FileMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceFileInfo? info =
            await workspace.Files.GetInfoAsync(
                path,
                cancellationToken).ConfigureAwait(false);
        FileMutationContent before;
        WorkspaceContentVersion? expectedVersion = null;
        if (info is null)
        {
            if (!request.AllowCreate)
            {
                throw new FileMutationException(
                    FileMutationErrorKind.FileNotFound,
                    BuildMissingMutationMessage(request.Path));
            }
            before = FileMutationContent.Missing(request.Path);
        }
        else
        {
            if (info.Kind == WorkspaceEntryKind.Directory)
            {
                throw new FileMutationException(
                    FileMutationErrorKind.PathIsDirectory,
                    "Cannot mutate a directory.");
            }
            WorkspaceTextSnapshot snapshot =
                (await workspace.Files.ReadTextAsync(
                    path,
                    new WorkspaceTextReadOptions
                    {
                        StartLine = 1,
                        MaxLines = null,
                        MaxBytes = checked((int)MaxMutableFileBytes),
                    },
                    cancellationToken).ConfigureAwait(false)) ??
                throw new FileMutationException(
                    FileMutationErrorKind.FileNotFound,
                    $"Workspace file '{request.Path}' does not exist.");
            if (snapshot.Truncated)
            {
                throw new FileMutationException(
                    FileMutationErrorKind.FileTooLarge,
                    "File is too large to mutate.");
            }
            expectedVersion = snapshot.Version;
            before = new FileMutationContent(
                Text: snapshot.Content,
                Exists: true,
                Encoding: new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true),
                HasBom: false,
                LineEnding: DetectLineEnding(snapshot.Content),
                LastWriteTimeUtc: snapshot.Version.ModifiedAt,
                ByteLength: snapshot.Version.Size.Value,
                ContentHash: ComputeContentHash(
                    snapshot.Content));
        }
        request.ValidateBeforeMutation?.Invoke(before);

        bool created = !before.Exists;
        string finalText = request.NormalizeLineEndingsToExistingFile
            ? NormalizeLineEndings(
                request.UpdatedText,
                before.LineEnding)
            : request.UpdatedText;
        bool changed = !before.Exists ||
            !string.Equals(
                before.Text,
                finalText,
                StringComparison.Ordinal);
        var notes = request.Notes
            .Take(MaxMutationEventNotes)
            .ToList();
        var textEdits =
            NormalizeMutationTextEdits(request.TextEdits);
        WorkspaceContentVersion finalVersion =
            expectedVersion ??
            new WorkspaceContentVersion(
                null,
                null,
                null,
                default);
        if (changed)
        {
            WorkspaceMutationResult write =
                await workspace.Files.WriteTextAsync(
                    path,
                    finalText,
                    new WorkspaceWriteCondition
                    {
                        ExpectedVersion = expectedVersion,
                        AllowCreate = request.AllowCreate,
                    },
                    cancellationToken).ConfigureAwait(false);
            if (write.Outcome !=
                WorkspaceMutationOutcome.Succeeded)
            {
                throw write.Outcome switch
                {
                    WorkspaceMutationOutcome.Stale =>
                        new FileMutationException(
                            FileMutationErrorKind.StaleFile,
                            write.Message ??
                                "File has been modified since it was read."),
                    WorkspaceMutationOutcome.AccessDenied =>
                        new UnauthorizedAccessException(
                            write.Message ??
                                "Workspace mutation was denied."),
                    WorkspaceMutationOutcome.AlreadyExists =>
                        new FileMutationException(
                            FileMutationErrorKind.StaleFile,
                            write.Message ??
                                "File already exists."),
                    WorkspaceMutationOutcome.NotFound =>
                        new FileMutationException(
                            FileMutationErrorKind.FileNotFound,
                            write.Message ??
                                "File was not found."),
                    _ => new FileMutationException(
                        FileMutationErrorKind.WriteFailed,
                        write.Message ??
                            "Workspace mutation failed."),
                };
            }
            finalVersion = write.Version ?? finalVersion;
        }

        long finalByteLength =
            Encoding.UTF8.GetByteCount(finalText);
        string finalHash = ComputeContentHash(finalText);
        DateTimeOffset finalLastWrite =
            finalVersion.ModifiedAt ?? DateTimeOffset.UtcNow;
        var (firstChangedLine, lastChangedLine) =
            GetChangedLineRange(before.Text, finalText);
        bool eventEmitted = false;
        if (changed)
        {
            SetFileMutationMetadata(
                request.FunctionContext,
                request.ToolName,
                request.Path,
                request.Kind,
                finalText.Length <= MaxEventSnapshotChars
                    ? finalText
                    : null,
                finalByteLength,
                finalLastWrite);
            if (request.EventFactory is not null)
            {
                FileMutationAppliedEvent mutationEvent =
                    BuildFileMutationEvent(
                        request with
                        {
                            TextEdits = textEdits,
                            Notes = notes,
                        },
                        before,
                        finalText,
                        finalByteLength,
                        finalHash,
                        finalLastWrite,
                        created,
                        changed);
                eventEmitted =
                    await EmitFileMutationEventAsync(
                        request.FunctionContext,
                        mutationEvent,
                        cancellationToken).ConfigureAwait(false);
            }
        }

        return new FileMutationResult(
            request.Path,
            created,
            changed,
            before.Text,
            finalText,
            finalByteLength,
            finalHash,
            finalLastWrite,
            firstChangedLine,
            lastChangedLine,
            textEdits,
            notes,
            eventEmitted);
    }

    /// <summary>
    /// Reads the current workspace content needed by write and edit policy
    /// checks. The returned version remains bound to this exact snapshot.
    /// </summary>
    internal static async ValueTask<FileMutationContent>
        ReadWorkspaceMutationContentAsync(
            RuntimeWorkspaceBinding workspace,
            WorkspacePath path,
            bool allowMissing,
            string displayPath,
            CancellationToken cancellationToken = default)
    {
        WorkspaceFileInfo? info =
            await workspace.Files.GetInfoAsync(
                path,
                cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            if (!allowMissing)
            {
                throw new FileMutationException(
                    FileMutationErrorKind.FileNotFound,
                    BuildMissingMutationMessage(displayPath));
            }
            return FileMutationContent.Missing(displayPath);
        }
        if (info.Kind == WorkspaceEntryKind.Directory)
        {
            throw new FileMutationException(
                FileMutationErrorKind.PathIsDirectory,
                "Cannot mutate a directory.");
        }
        if (info.Version.Size.Value > MaxMutableFileBytes)
        {
            throw new FileMutationException(
                FileMutationErrorKind.FileTooLarge,
                "File is too large to mutate.");
        }
        WorkspaceBinarySnapshot? sample =
            await workspace.Files.ReadBytesAsync(
                path,
                new WorkspaceByteReadOptions
                {
                    MaxBytes = 4096,
                },
                cancellationToken).ConfigureAwait(false);
        if (sample is not null &&
            LooksBinary(
                sample.Content.ToArray(),
                DetectBomEncoding(sample.Content.ToArray()) is not null))
        {
            throw new FileMutationException(
                FileMutationErrorKind.BinaryFile,
                "Cannot mutate binary file.");
        }
        WorkspaceTextSnapshot snapshot =
            (await workspace.Files.ReadTextAsync(
                path,
                new WorkspaceTextReadOptions
                {
                    StartLine = 1,
                    MaxLines = null,
                    MaxBytes = checked((int)MaxMutableFileBytes),
                },
                cancellationToken).ConfigureAwait(false)) ??
            throw new FileMutationException(
                FileMutationErrorKind.FileNotFound,
                $"Workspace file '{displayPath}' does not exist.");
        if (snapshot.Truncated)
        {
            throw new FileMutationException(
                FileMutationErrorKind.FileTooLarge,
                "File is too large to mutate.");
        }
        return new FileMutationContent(
            Text: snapshot.Content,
            Exists: true,
            Encoding: new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            HasBom: false,
            LineEnding: DetectLineEnding(snapshot.Content),
            LastWriteTimeUtc: snapshot.Version.ModifiedAt,
            ByteLength: snapshot.Version.Size.Value,
            ContentHash: ComputeContentHash(snapshot.Content));
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
        FunctionExecutionContext? context,
        string toolName,
        string path,
        CodingFileMutationKind kind,
        string? text,
        long byteLength,
        DateTimeOffset lastWriteTimeUtc)
    {
        if (context?.ResultMetadata is null)
            return;

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

    private static async ValueTask<bool> EmitFileMutationEventAsync(
        FunctionExecutionContext context,
        FileMutationAppliedEvent mutationEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.TryPublishAsync(mutationEvent, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private static string FormatMutationError(string toolName, FileMutationErrorKind kind, string? path, string message)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);
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
            ToolCallId: context?.FunctionCallId ?? string.Empty,
            FunctionName: context?.FunctionName ?? request.ToolName,
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
        => "File does not exist.";

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

internal sealed record FileMutationDiffLine(
    char Sign,
    string Text,
    int? OldLine,
    int? NewLine);

internal sealed record FileMutationDiffWindow(int Start, int End);

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

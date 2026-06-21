using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

public partial class CodingToolHarness
{
    [AIFunction]
    [RequiresPermission]
    [Description("Creates or completely rewrites a text file. Use for new files or full-file replacement when you can provide the complete intended content. For existing non-empty files, ReadFile must have shown the entire file before WriteFile can rewrite it. Use EditFile for targeted changes to existing files. Do not create documentation files unless requested. The content parameter is the full file body and its line endings are preserved.")]
    public async Task<object> WriteFile(
        [Description("The file path to write. Relative paths are resolved from the current working directory.")] string path,
        [Description("The complete intended file content to write.")] string content,
        FunctionExecutionContext context)
    {
        ResolvedMutationPath? resolvedPath = null;
        try
        {
            var argumentError = ValidateWriteArguments(path, content);
            if (argumentError != null)
                return FormatWriteError(WriteFileErrorKind.InvalidArguments, path, argumentError);

            resolvedPath = ResolveMutationPath(path);
            ValidateMutationPath(resolvedPath);

            if (ContainsStandaloneOmissionPlaceholder(content))
                return FormatWriteError(
                    WriteFileErrorKind.NewOmissionPlaceholder,
                    resolvedPath.FullPath,
                    "Content contains an omission placeholder. Provide complete file content.");

            var current = await ReadMutationContentAsync(resolvedPath.FullPath, allowMissing: true).ConfigureAwait(false);
            var validation = ValidateWritePolicy(resolvedPath.FullPath, current, context);
            if (validation.ErrorKind != null)
                return FormatWriteError(validation.ErrorKind.Value, resolvedPath.FullPath, validation.ErrorMessage!);

            var mode = validation.Mode;
            var mutationKind = mode == FileWriteMode.Create
                ? CodingFileMutationKind.Created
                : CodingFileMutationKind.Changed;
            var textEdit = BuildWholeFileTextEdit(current.Text, content);

            var result = await ApplyTextMutationAsync(
                new FileMutationRequest(
                    ToolName: "WriteFile",
                    Path: resolvedPath.FullPath,
                    UpdatedText: content,
                    Kind: mutationKind,
                    AllowCreate: true,
                    CreateParentDirectories: true,
                    NormalizeLineEndingsToExistingFile: false,
                    TextEdits: [textEdit],
                    Notes: [],
                    FunctionContext: context,
                    ValidateBeforeMutation: before => ValidateWriteBeforeMutation(resolvedPath.FullPath, validation.PriorRead, before),
                    EventFactory: request => BuildFileWriteAppliedEvent(request, mode))).ConfigureAwait(false);

            return FormatWriteResult(result, mode);
        }
        catch (FileMutationException exception) when (exception.Kind == FileMutationErrorKind.StaleFile)
        {
            return FormatWriteError(
                WriteFileErrorKind.StaleRead,
                resolvedPath?.FullPath ?? path,
                "File has been modified since it was read. Read it again before rewriting it.");
        }
        catch (FileMutationException exception)
        {
            return FormatMutationError("WriteFile", exception.Kind, resolvedPath?.FullPath ?? path, exception.Message);
        }
        catch (DecoderFallbackException)
        {
            return FormatMutationError("WriteFile", FileMutationErrorKind.DecodeError, resolvedPath?.FullPath ?? path, "Unable to decode file as text.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FormatMutationError("WriteFile", FileMutationErrorKind.WriteFailed, resolvedPath?.FullPath ?? path, $"Unable to write file: {exception.Message}");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FormatWriteError(WriteFileErrorKind.InvalidArguments, path, $"Invalid path: {exception.Message}");
        }
    }

    private static string? ValidateWriteArguments(string? path, string? content)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Path is required.";

        if (content == null)
            return "Content is required.";

        return null;
    }

    private static bool ContainsStandaloneOmissionPlaceholder(string content)
        => StandaloneOmissionPlaceholderRegex().IsMatch(content);

    private static WriteFileValidationResult ValidateWritePolicy(
        string fullPath,
        FileMutationContent current,
        FunctionExecutionContext context)
    {
        if (!current.Exists)
            return new WriteFileValidationResult(FileWriteMode.Create, RequiresFullRead: false, null, null, null);

        if (current.ByteLength == 0)
            return new WriteFileValidationResult(FileWriteMode.FillEmpty, RequiresFullRead: false, null, null, null);

        var readFileStateKey = typeof(ReadFileState).FullName!;
        var compactionStateKey = typeof(CompactionStateData).FullName!;
        var priorRead = context
            .Analyze(state => state.MiddlewareState.GetState<ReadFileState>(readFileStateKey))
            ?.FilesByPath.GetValueOrDefault(fullPath);

        if (priorRead == null)
        {
            return new WriteFileValidationResult(
                FileWriteMode.Rewrite,
                RequiresFullRead: true,
                null,
                WriteFileErrorKind.NotRead,
                "Existing non-empty file must be fully read before rewriting it.");
        }

        if (priorRead.Coverage != ReadFileCoverage.FullFile)
        {
            return new WriteFileValidationResult(
                FileWriteMode.Rewrite,
                RequiresFullRead: true,
                priorRead,
                WriteFileErrorKind.PartialRead,
                "Existing non-empty file was only partially read. Read the full file before rewriting it.");
        }

        var compactionState = context
            .Analyze(state => state.MiddlewareState.GetState<CompactionStateData>(compactionStateKey));
        if (compactionState?.LastAppliedAt > priorRead.ReadAt)
        {
            return new WriteFileValidationResult(
                FileWriteMode.Rewrite,
                RequiresFullRead: false,
                priorRead,
                WriteFileErrorKind.HistoryReducedRead,
                "The previous ReadFile result may no longer be visible in context. Read the file again before rewriting it.");
        }

        try
        {
            ValidateWriteBeforeMutation(fullPath, priorRead, current);
        }
        catch (FileMutationException exception) when (exception.Kind == FileMutationErrorKind.StaleFile)
        {
            return new WriteFileValidationResult(
                FileWriteMode.Rewrite,
                RequiresFullRead: false,
                priorRead,
                WriteFileErrorKind.StaleRead,
                "File has been modified since it was read. Read it again before rewriting it.");
        }

        return new WriteFileValidationResult(FileWriteMode.Rewrite, RequiresFullRead: false, priorRead, null, null);
    }

    private static void ValidateWriteBeforeMutation(string fullPath, ReadFileSnapshot? priorRead, FileMutationContent current)
    {
        if (!current.Exists || current.ByteLength == 0)
            return;

        if (priorRead == null)
            throw new FileMutationException(FileMutationErrorKind.StaleFile, "Existing file has not been fully read.");

        if (priorRead.Coverage != ReadFileCoverage.FullFile)
            throw new FileMutationException(FileMutationErrorKind.StaleFile, "Existing file was only partially read.");

        if (string.Equals(priorRead.Path, fullPath, StringComparison.Ordinal) &&
            priorRead.Length == current.ByteLength &&
            priorRead.LastWriteTimeUtc == current.LastWriteTimeUtc)
        {
            return;
        }

        if (priorRead.ReturnedContentHash == ComputeWriteContentFallbackHash(current.Text))
            return;

        throw new FileMutationException(FileMutationErrorKind.StaleFile, "File has been modified since it was read.");
    }

    private static FileMutationTextEdit BuildWholeFileTextEdit(string before, string after)
        => new(
            1,
            GetRange(before, 0, before.Length),
            GetRange(after, 0, after.Length),
            before,
            after,
            TextOmitted: false,
            OmissionReason: null);

    private static string FormatWriteResult(FileMutationResult result, FileWriteMode mode)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);
        writer.WriteStartElement("write_file");
        writer.WriteAttributeString("path", result.Path);
        writer.WriteAttributeString("mode", FormatEnum(mode));
        writer.WriteAttributeString("changed", result.Changed.ToString().ToLowerInvariant());
        writer.WriteAttributeString("created", result.Created.ToString().ToLowerInvariant());
        writer.WriteAttributeString("content_hash", result.ContentHash);
        writer.WriteAttributeString("byte_length", result.ByteLength.ToString(CultureInfo.InvariantCulture));
        if (result.FirstChangedLine.HasValue)
            writer.WriteAttributeString("first_changed_line", result.FirstChangedLine.Value.ToString(CultureInfo.InvariantCulture));
        if (result.LastChangedLine.HasValue)
            writer.WriteAttributeString("last_changed_line", result.LastChangedLine.Value.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("event_emitted", result.EventEmitted.ToString().ToLowerInvariant());
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static string FormatWriteError(WriteFileErrorKind kind, string? path, string message)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);
        writer.WriteStartElement("error");
        writer.WriteAttributeString("tool", "WriteFile");
        writer.WriteAttributeString("kind", FormatEnum(kind));
        if (!string.IsNullOrWhiteSpace(path))
            writer.WriteAttributeString("path", path);
        writer.WriteString(message);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static FileWriteAppliedEvent BuildFileWriteAppliedEvent(FileMutationEventBuildRequest request, FileWriteMode mode)
        => new()
        {
            ToolCallId = request.ToolCallId,
            FunctionName = request.FunctionName,
            Path = request.Path,
            DisplayPath = request.DisplayPath,
            MutationKind = request.MutationKind,
            Created = request.Created,
            Changed = request.Changed,
            Before = request.Before,
            After = request.After,
            TextEdits = request.TextEdits,
            Hunks = request.Hunks,
            HunksTruncated = request.HunksTruncated,
            DiffStat = request.DiffStat,
            Notes = request.Notes,
            Mode = mode
        };

    private static string ComputeWriteContentFallbackHash(string text)
    {
        using var reader = new StringReader(text);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
            lines.Add(line);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [GeneratedRegex(@"(?m)^\s*(?:(?://\s*)?\((?:rest of (?:methods|code)|unchanged code) \.\.\.\)|//\s*(?:rest of (?:methods|code)|unchanged code) \.\.\.)\s*$")]
    private static partial Regex StandaloneOmissionPlaceholderRegex();
}

internal sealed record WriteFileValidationResult(
    FileWriteMode Mode,
    bool RequiresFullRead,
    ReadFileSnapshot? PriorRead,
    WriteFileErrorKind? ErrorKind,
    string? ErrorMessage);

internal enum WriteFileErrorKind
{
    InvalidArguments,
    NotRead,
    PartialRead,
    HistoryReducedRead,
    StaleRead,
    NewOmissionPlaceholder
}

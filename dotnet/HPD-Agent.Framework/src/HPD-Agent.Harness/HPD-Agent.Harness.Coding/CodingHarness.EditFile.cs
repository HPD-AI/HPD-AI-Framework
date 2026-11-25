using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HPD.Agent;
using HPD.Agent.Sandbox;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

public partial class CodingToolHarness
{
    [AIFunction]
    [RequiresPermission]
    [Description("Edits a text file by applying one or more targeted string replacements. Use after ReadFile has shown the relevant content. The oldString values should match file content exactly and uniquely unless replaceAll is true; the tool may recover from mechanical mismatches such as line endings, escaping, quotes, indentation, or whitespace when the target is still unique and inside the read range. Use WriteFile for whole-file rewrites, Grep to find unknown text, and ReadFile to inspect context before editing. Do not include ReadFile line-number prefixes in oldString or newString.")]
    public async Task<string> EditFile(
        [Description("The file path to edit. Relative paths are resolved from the current working directory.")] string path,
        [Description("One or more targeted replacements to apply sequentially.")] IReadOnlyList<FileEditReplacement> edits,
        FunctionExecutionContext context)
    {
        string? resolvedDisplayPath = null;
        try
        {
            var harnessConfig = _config.For(context);
            harnessConfig.ValidateSettings();
            var editConfig = harnessConfig.EditFile;
            var mutationConfig = harnessConfig.Files;
            var argumentError = ValidateEditArguments(
                path,
                edits,
                editConfig);
            if (argumentError != null)
                return FormatEditError(EditFileErrorKind.InvalidArguments, path, argumentError);

            if (!CodingWorkspacePathAccess.TryGet(context, out var workspace))
            {
                return FormatEditError(
                    EditFileErrorKind.InvalidArguments,
                    path,
                    "No Environment workspace binding is available for EditFile.");
            }

            WorkspacePath logical =
                CodingWorkspacePathAccess.Resolve(workspace, path);
            resolvedDisplayPath =
                CodingWorkspacePathAccess.Display(workspace, logical);
            return await EditWorkspaceFileAsync(
                workspace,
                logical,
                resolvedDisplayPath,
                edits,
                editConfig,
                mutationConfig,
                context).ConfigureAwait(false);
        }
        catch (FileMutationException exception) when (exception.Kind == FileMutationErrorKind.StaleFile)
        {
            return FormatEditError(
                EditFileErrorKind.StaleRead,
                resolvedDisplayPath ?? path,
                "File has been modified since it was read. Read it again before editing.");
        }
        catch (FileMutationException exception)
        {
            return FormatMutationError("EditFile", exception.Kind, resolvedDisplayPath ?? path, exception.Message);
        }
        catch (DecoderFallbackException)
        {
            return FormatMutationError("EditFile", FileMutationErrorKind.DecodeError, resolvedDisplayPath ?? path, "Unable to decode file as text.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FormatMutationError("EditFile", FileMutationErrorKind.WriteFailed, resolvedDisplayPath ?? path, $"Unable to edit file: {exception.Message}");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FormatEditError(EditFileErrorKind.InvalidArguments, resolvedDisplayPath ?? path, $"Invalid path: {exception.Message}");
        }
    }

    private async Task<string> EditWorkspaceFileAsync(
        RuntimeWorkspaceBinding workspace,
        WorkspacePath logical,
        string displayPath,
        IReadOnlyList<FileEditReplacement> edits,
        EditFileConfig editConfig,
        FileMutationConfig mutationConfig,
        FunctionExecutionContext context)
    {
        WorkspaceRootBinding root =
            CodingWorkspacePathAccess.GetRoot(workspace, logical);
        if (root.Access == AccessMode.ReadOnly)
        {
            return FormatEditError(
                EditFileErrorKind.InvalidArguments,
                displayPath,
                "Workspace root is read-only.");
        }
        if (IsNotebookPath(displayPath))
        {
            throw new FileMutationException(
                FileMutationErrorKind.NotebookFile,
                "File is a notebook. Use NotebookEdit when available.");
        }

        var invalidEmptyIndex = FindInvalidEmptyOldString(edits);
        if (invalidEmptyIndex >= 0 && !IsCreateEdit(edits))
        {
            return FormatEditError(
                EditFileErrorKind.InvalidEmptyOldString,
                displayPath,
                $"OldString is required for edit {invalidEmptyIndex + 1} unless creating or filling an empty file.");
        }

        bool allowMissing = IsCreateEdit(edits);
        ReadFileSnapshot? knownPriorRead =
            FindPriorRead(displayPath, context);
        FileMutationContent current =
            await ReadWorkspaceMutationContentAsync(
                workspace,
                logical,
                allowMissing || knownPriorRead != null,
                displayPath,
                mutationConfig).ConfigureAwait(false);
        if (current.Exists &&
            current.ByteLength > 0 &&
            allowMissing)
        {
            return FormatEditError(
                EditFileErrorKind.InvalidEmptyOldString,
                displayPath,
                "Cannot edit a non-empty file with an empty OldString.");
        }

        EditReadValidationResult priorRead =
            ValidateEditReadPolicy(
                displayPath,
                current,
                allowMissing,
                knownPriorRead);
        if (priorRead.ErrorKind != null)
        {
            return FormatEditError(
                priorRead.ErrorKind.Value,
                displayPath,
                priorRead.ErrorMessage!);
        }
        EditApplicationResult application =
            ApplyEdits(
                current,
                edits,
                priorRead.PriorRead,
                editConfig);
        if (application.ErrorKind != null)
        {
            return FormatEditError(
                application.ErrorKind.Value,
                displayPath,
                application.ErrorMessage!,
                application.ErrorStrategy);
        }

        CodingFileMutationKind mutationKind =
            application.Created
                ? CodingFileMutationKind.Created
                : CodingFileMutationKind.Changed;
        FileMutationResult result =
            await ApplyWorkspaceTextMutationAsync(
                workspace,
                logical,
                new FileMutationRequest(
                    ToolName: "EditFile",
                    Path: displayPath,
                    UpdatedText: application.UpdatedContent,
                    Kind: mutationKind,
                    AllowCreate: application.Created,
                    CreateParentDirectories: application.Created,
                    NormalizeLineEndingsToExistingFile:
                        current.Exists,
                    TextEdits: application.TextEdits,
                    Notes: application.NormalizationNotes
                        .Take(editConfig.MaxNormalizationNotes)
                        .Select(note =>
                            new FileMutationNote(
                                note.EditIndex,
                                note.Kind,
                                note.Count))
                        .ToArray(),
                    FunctionContext: context,
                    Config: mutationConfig,
                    ValidateBeforeMutation: before =>
                        ValidateEditBeforeMutation(
                            displayPath,
                            priorRead.PriorRead,
                            before,
                            application.Created),
                    EventFactory: request =>
                        BuildFileEditAppliedEvent(
                            request,
                            application)))
                .ConfigureAwait(false);
        return FormatEditResult(result, application);
    }

    private static string? ValidateEditArguments(
        string? path,
        IReadOnlyList<FileEditReplacement>? edits,
        EditFileConfig config)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Path is required.";

        if (edits == null)
            return "At least one edit is required.";

        if (edits.Count == 0)
            return "At least one edit is required.";

        if (edits.Count > config.MaxEdits)
            return $"Too many edits. Maximum is {config.MaxEdits}.";

        for (var i = 0; i < edits.Count; i++)
        {
            var editNumber = i + 1;
            var edit = edits[i];
            if (edit is null)
                return $"Null edit entry at edit {editNumber} is invalid.";

            if (edit.OldString == null)
                return $"OldString is required for edit {editNumber} unless creating or filling an empty file.";

            if (edit.NewString == null)
                return $"NewString is required for edit {editNumber}.";

            if (edit.OldString == edit.NewString)
                return $"OldString and NewString must be different for edit {editNumber}.";
        }

        return null;
    }

    private static bool IsCreateEdit(IReadOnlyList<FileEditReplacement> edits)
        => edits.Count == 1 && edits[0].OldString.Length == 0;

    private static int FindInvalidEmptyOldString(
        IReadOnlyList<FileEditReplacement> edits)
    {
        for (var i = 0; i < edits.Count; i++)
        {
            if (edits[i].OldString.Length == 0)
                return i;
        }

        return -1;
    }

    private static EditReadValidationResult ValidateEditReadPolicy(
        string fullPath,
        FileMutationContent current,
        bool allowMissingCreate,
        ReadFileSnapshot? priorRead)
    {
        if (!current.Exists)
        {
            if (priorRead != null)
            {
                return new EditReadValidationResult(
                    priorRead,
                    EditFileErrorKind.StaleRead,
                    "File was deleted after it was read. Read it again before editing.");
            }

            return allowMissingCreate
                ? new EditReadValidationResult(null, null, null)
                : new EditReadValidationResult(null, EditFileErrorKind.NoMatch, "String to replace was not found for edit 1.");
        }

        if (current.ByteLength == 0)
        {
            if (priorRead is { Length: > 0 })
            {
                return new EditReadValidationResult(
                    priorRead,
                    EditFileErrorKind.StaleRead,
                    "File was emptied after it was read. Read it again before editing.");
            }

            return new EditReadValidationResult(null, null, null);
        }

        if (priorRead == null)
        {
            return new EditReadValidationResult(
                null,
                EditFileErrorKind.NotRead,
                "File has not been read yet. Read it first before editing.");
        }

        try
        {
            ValidateEditBeforeMutation(fullPath, priorRead, current, creating: false);
        }
        catch (FileMutationException exception) when (exception.Kind == FileMutationErrorKind.StaleFile)
        {
            return new EditReadValidationResult(
                priorRead,
                EditFileErrorKind.StaleRead,
                "File has been modified since it was read. Read it again before editing.");
        }

        return new EditReadValidationResult(priorRead, null, null);
    }

    private static ReadFileSnapshot? FindPriorRead(
        string fullPath,
        FunctionExecutionContext context)
    {
        var readFileStateKey = typeof(ReadFileState).FullName!;
        return context
            .Analyze(state => state.MiddlewareState.GetState<ReadFileState>(readFileStateKey))
            ?.FilesByPath.GetValueOrDefault(fullPath);
    }

    private static void ValidateEditBeforeMutation(
        string fullPath,
        ReadFileSnapshot? priorRead,
        FileMutationContent current,
        bool creating)
    {
        if (creating)
        {
            if (!current.Exists)
                return;

            throw new FileMutationException(
                FileMutationErrorKind.StaleFile,
                "File was created after edit validation.");
        }

        if (!current.Exists)
        {
            throw new FileMutationException(
                FileMutationErrorKind.StaleFile,
                "File was deleted before the edit could be committed.");
        }

        if (current.ByteLength == 0)
        {
            if (priorRead is { Length: > 0 })
            {
                throw new FileMutationException(
                    FileMutationErrorKind.StaleFile,
                    "File was emptied before the edit could be committed.");
            }

            return;
        }

        if (priorRead == null)
            throw new FileMutationException(FileMutationErrorKind.StaleFile, "File has not been read yet.");

        if (priorRead.Path != fullPath)
            throw new FileMutationException(FileMutationErrorKind.StaleFile, "File has been modified since it was read.");

        if (priorRead.SourceContentHash != null)
        {
            if (string.Equals(
                    priorRead.SourceContentHash,
                    current.SourceVersionHash,
                    StringComparison.Ordinal))
            {
                return;
            }

            throw new FileMutationException(
                FileMutationErrorKind.StaleFile,
                "File has been modified since it was read.");
        }

        if (priorRead.Length == current.ByteLength &&
            priorRead.LastWriteTimeUtc == current.LastWriteTimeUtc)
        {
            return;
        }

        if (priorRead.Coverage == ReadFileCoverage.FullFile &&
            priorRead.ReturnedContentHash == ComputeEditReturnedContentHash(current.Text))
        {
            return;
        }

        throw new FileMutationException(FileMutationErrorKind.StaleFile, "File has been modified since it was read.");
    }

    private static EditApplicationResult ApplyEdits(
        FileMutationContent current,
        IReadOnlyList<FileEditReplacement> edits,
        ReadFileSnapshot? priorRead,
        EditFileConfig config)
    {
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            if (ContainsNewOmissionPlaceholder(
                    edit.OldString,
                    edit.NewString))
            {
                return EditApplicationResult.Error(
                    EditFileErrorKind.NewOmissionPlaceholder,
                    $"NewString for edit {i + 1} contains an omission placeholder. Provide exact literal replacement text.");
            }
        }

        if (!current.Exists)
        {
            if (!IsCreateEdit(edits))
                return EditApplicationResult.Error(EditFileErrorKind.NoMatch, "String to replace was not found for edit 1.");

            var newText = edits[0].NewString;
            IReadOnlyList<EditNormalizationNote> createNotes = [];
            return new EditApplicationResult(
                OriginalContent: string.Empty,
                UpdatedContent: newText,
                TextEdits:
                [
                    new FileMutationTextEdit(
                        1,
                        GetRange(string.Empty, 0, 0),
                        GetRange(newText, 0, newText.Length),
                        string.Empty,
                        newText,
                        TextOmitted: false,
                        OmissionReason: null)
                ],
                ResolvedReplacements:
                [
                    new ResolvedEditReplacement(
                        1,
                        string.Empty,
                        string.Empty,
                        newText,
                        ReplaceAll: false,
                        EditMatchStrategy.Create,
                        Recovered: false,
                        createNotes)
                ],
                ReplacementCount: 1,
                Created: true,
                NormalizationNotes: createNotes);
        }

        if (current.ByteLength == 0 && IsCreateEdit(edits))
        {
            var newText = edits[0].NewString;
            IReadOnlyList<EditNormalizationNote> fillNotes = [];
            return new EditApplicationResult(
                OriginalContent: string.Empty,
                UpdatedContent: newText,
                TextEdits:
                [
                    new FileMutationTextEdit(
                        1,
                        GetRange(string.Empty, 0, 0),
                        GetRange(newText, 0, newText.Length),
                        string.Empty,
                        newText,
                        TextOmitted: false,
                        OmissionReason: null)
                ],
                ResolvedReplacements:
                [
                    new ResolvedEditReplacement(
                        1,
                        string.Empty,
                        string.Empty,
                        newText,
                        ReplaceAll: false,
                        EditMatchStrategy.Create,
                        Recovered: false,
                        fillNotes)
                ],
                ReplacementCount: 1,
                Created: false,
                NormalizationNotes: fillNotes);
        }

        if (current.ByteLength > 0 && IsCreateEdit(edits))
            return EditApplicationResult.Error(EditFileErrorKind.InvalidEmptyOldString, "Cannot edit a non-empty file with an empty OldString.");

        var buffer = current.Text;
        var originMap = EditOriginMap.Create(buffer.Length);
        var readCoverage = GetEditReadCoverage(buffer, priorRead);
        var textEdits = new List<FileMutationTextEdit>();
        var resolvedReplacements = new List<ResolvedEditReplacement>();
        var normalizationNotes = new List<EditNormalizationNote>();
        var replacementCount = 0;

        for (var i = 0; i < edits.Count; i++)
        {
            var editIndex = i + 1;
            var edit = edits[i];

            var normalizedNewString = edit.NewString;
            var resolution = ResolveReplacement(
                buffer,
                edit,
                normalizedNewString,
                editIndex,
                current.HasBom,
                originMap,
                readCoverage,
                config);
            if (resolution.ErrorKind != null)
                return EditApplicationResult.Error(resolution.ErrorKind.Value, resolution.ErrorMessage!, resolution.ErrorStrategy);

            var resolved = resolution.Resolved!;
            var ranges = resolution.Ranges!;
            var beforeBuffer = buffer;
            var touchesEof = ranges.Any(
                range => range.Start + range.Length == beforeBuffer.Length);
            if (!EndsWithAnyNewline(beforeBuffer) &&
                EndsWithAnyNewline(resolved.ActualNewString) &&
                touchesEof)
            {
                if (ranges.Count > 1)
                {
                    return EditApplicationResult.Error(
                        EditFileErrorKind.InvalidArguments,
                        $"Edit {editIndex} combines an EOF match with interior matches while NewString ends in a newline. Split the edit so each replacement has one newline policy.");
                }

                resolved = resolved with
                {
                    ActualNewString = TrimTrailingNewlines(resolved.ActualNewString),
                    NormalizationNotes = resolved.NormalizationNotes
                        .Concat([new EditNormalizationNote(editIndex, "trailing_newline_preserved")])
                        .ToArray()
                };
            }

            var beforeRanges = ranges.Select(range => GetRange(beforeBuffer, range.Start, range.Length)).ToArray();
            var orderedRanges = ranges.OrderBy(range => range.Start).ToArray();
            var afterRangeStarts = new Dictionary<EditCandidate, int>();
            var offsetDelta = 0;
            foreach (var range in orderedRanges)
            {
                afterRangeStarts[range] = range.Start + offsetDelta;
                offsetDelta += resolved.ActualNewString.Length - range.Length;
            }

            var originUpdate = originMap.ApplyReplacements(
                orderedRanges,
                resolved.ActualNewString.Length);
            if (originUpdate.Status == EditOriginMapApplyStatus.InvalidRange)
            {
                return EditApplicationResult.Error(
                    EditFileErrorKind.InvalidArguments,
                    $"Edit {editIndex} produced an invalid replacement range.");
            }

            if (originUpdate.Status == EditOriginMapApplyStatus.CoverageGap)
            {
                return EditApplicationResult.Error(
                    EditFileErrorKind.InternalError,
                    $"Edit {editIndex} could not be applied safely. Read the file again and retry.");
            }

            originMap = originUpdate.Map!;
            buffer = ApplyReplacementsToBuffer(
                beforeBuffer,
                orderedRanges,
                resolved.ActualNewString);

            if (string.Equals(beforeBuffer, buffer, StringComparison.Ordinal))
                return EditApplicationResult.Error(EditFileErrorKind.NoChange, $"Edit {editIndex} would not change the file.");

            var afterRanges = ranges
                .Select(range => GetRange(buffer, afterRangeStarts[range], resolved.ActualNewString.Length))
                .ToArray();
            textEdits.AddRange(ranges.Select((range, rangeIndex) => new FileMutationTextEdit(
                editIndex,
                beforeRanges[rangeIndex],
                afterRanges[rangeIndex],
                beforeBuffer.Substring(range.Start, range.Length),
                resolved.ActualNewString,
                TextOmitted: false,
                OmissionReason: null)));

            resolvedReplacements.Add(resolved);
            normalizationNotes.AddRange(resolved.NormalizationNotes);
            replacementCount += ranges.Count;
        }

        return new EditApplicationResult(
            current.Text,
            buffer,
            textEdits,
            resolvedReplacements,
            replacementCount,
            Created: false,
            normalizationNotes.Take(config.MaxNormalizationNotes).ToArray());
    }

    private static EditReplacementResolution ResolveReplacement(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        bool fileHasBom,
        EditOriginMap originMap,
        EditReadCoverage readCoverage,
        EditFileConfig config)
    {
        if (edit.OldString.Length == 0)
            return EditReplacementResolution.Error(EditFileErrorKind.InvalidEmptyOldString, "Cannot edit a non-empty file with an empty OldString.");

        if (normalizedNewString.Length == 0 && !EndsWithAnyNewline(edit.OldString))
        {
            var deleteCandidates = CollectCandidates(
                EnumerateDeleteLineCandidates(buffer, edit.OldString),
                originMap,
                GetCandidateCollectionLimit(config, recovered: false));
            var deleteLine = ResolveCandidateSet(
                buffer,
                edit.OldString,
                normalizedNewString,
                edit.ReplaceAll,
                editIndex,
                EditMatchStrategy.Exact,
                recovered: false,
                originMap,
                readCoverage,
                config,
                [],
                deleteCandidates);
            if (deleteLine.Resolved != null || IsTerminalCandidateError(deleteLine.ErrorKind))
                return deleteLine;
        }

        var exact = ResolveCandidateSet(
            buffer,
            edit.OldString,
            edit.OldString,
            normalizedNewString,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.Exact,
            recovered: false,
            originMap,
            readCoverage,
            config,
            []);
        if (exact.Resolved != null || IsTerminalCandidateError(exact.ErrorKind))
            return exact;

        if (edit.OldString.Length < config.MinRecoveryOldStringChars)
            return EditReplacementResolution.Error(EditFileErrorKind.NoMatch, $"String to replace was not found for edit {editIndex}.");

        var lineEnding = TryResolveLineEndingNormalizedMatch(buffer, edit, normalizedNewString, editIndex, originMap, readCoverage, config);
        if (lineEnding.HasResult)
            return lineEnding.Resolution;

        var bom = TryResolveBomHiddenFirstLineMatch(buffer, edit, normalizedNewString, editIndex, originMap, readCoverage, config, fileHasBom);
        if (bom.HasResult)
            return bom.Resolution;

        if (config.Recovery.Desanitization &&
            TryDesanitize(
                edit.OldString,
                config.Recovery.DesanitizationMap,
                out var desanitized,
                out var desanitizeKind))
        {
            var desanitizedResult = ResolveCandidateSet(
                buffer,
                edit.OldString,
                desanitized,
                normalizedNewString,
                edit.ReplaceAll,
                editIndex,
                EditMatchStrategy.Desanitized,
                recovered: true,
                originMap,
                readCoverage,
                config,
                [new EditNormalizationNote(editIndex, desanitizeKind)]);
            if (desanitizedResult.Resolved != null || IsRecoverySpecificError(desanitizedResult.ErrorKind))
                return desanitizedResult;
        }

        if (config.Recovery.QuoteNormalization)
        {
            var quote = TryResolveQuoteNormalizedMatch(buffer, edit, normalizedNewString, editIndex, originMap, readCoverage, config);
            if (quote.HasResult)
                return quote.Resolution;
        }

        if (config.Recovery.EscapedStringNormalization)
        {
            var unescaped = UnescapeSearchString(
                edit.OldString,
                out var unescapedChanged);
            if (unescapedChanged)
            {
                var unescapedResult = ResolveCandidateSet(
                    buffer,
                    edit.OldString,
                    unescaped,
                    normalizedNewString,
                    edit.ReplaceAll,
                    editIndex,
                    EditMatchStrategy.EscapedStringNormalized,
                    recovered: true,
                    originMap,
                    readCoverage,
                    config,
                    [new EditNormalizationNote(editIndex, "escaped_string")]);
                if (unescapedResult.Resolved != null ||
                    IsRecoverySpecificError(unescapedResult.ErrorKind))
                {
                    return unescapedResult;
                }
            }
        }

        var trimmed = edit.OldString.Trim();
        if (config.Recovery.TrimmedBoundary &&
            trimmed.Length != edit.OldString.Length &&
            trimmed.Length >= config.MinRecoveryOldStringChars)
        {
            var trimmedNewString = ApplyOldBoundaryTrimmingToNewString(
                edit.OldString,
                normalizedNewString,
                out var replacementTrimmed);
            var notes = replacementTrimmed
                ? new[]
                {
                    new EditNormalizationNote(editIndex, "trimmed_boundary"),
                    new EditNormalizationNote(editIndex, "replacement_boundary_trimmed")
                }
                : [new EditNormalizationNote(editIndex, "trimmed_boundary")];
            var trimmedResult = ResolveCandidateSet(
                buffer,
                edit.OldString,
                trimmed,
                trimmedNewString,
                edit.ReplaceAll,
                editIndex,
                EditMatchStrategy.TrimmedBoundary,
                recovered: true,
                originMap,
                readCoverage,
                config,
                notes);
            if (trimmedResult.Resolved != null || IsRecoverySpecificError(trimmedResult.ErrorKind))
                return trimmedResult;
        }

        if (config.Recovery.IndentationOnlyBlock)
        {
            var indentation = TryResolveIndentationOnlyBlockMatch(buffer, edit, normalizedNewString, editIndex, originMap, readCoverage, config);
            if (indentation.HasResult)
                return indentation.Resolution;
        }

        return EditReplacementResolution.Error(EditFileErrorKind.NoMatch, $"String to replace was not found for edit {editIndex}.");
    }

    private static EditReplacementResolution ResolveCandidateSet(
        string buffer,
        string requestedOldString,
        string actualOldString,
        string actualNewString,
        bool replaceAll,
        int editIndex,
        EditMatchStrategy strategy,
        bool recovered,
        EditOriginMap originMap,
        EditReadCoverage readCoverage,
        EditFileConfig config,
        IReadOnlyList<EditNormalizationNote> notes)
    {
        var collection = CollectCandidates(
            EnumerateOccurrences(buffer, actualOldString)
                .Select(start => new EditCandidate(
                    start,
                    actualOldString.Length)),
            originMap,
            GetCandidateCollectionLimit(config, recovered));

        return ResolveCandidateSet(
            buffer,
            requestedOldString,
            actualNewString,
            replaceAll,
            editIndex,
            strategy,
            recovered,
            originMap,
            readCoverage,
            config,
            notes,
            collection);
    }

    private static EditReplacementResolution ResolveCandidateSet(
        string buffer,
        string requestedOldString,
        string actualNewString,
        bool replaceAll,
        int editIndex,
        EditMatchStrategy strategy,
        bool recovered,
        EditOriginMap originMap,
        EditReadCoverage readCoverage,
        EditFileConfig config,
        IReadOnlyList<EditNormalizationNote> notes,
        EditCandidateCollection collection)
    {
        var selection = SelectCandidateSet(
            buffer,
            replaceAll,
            editIndex,
            strategy,
            recovered,
            originMap,
            readCoverage,
            config,
            notes,
            collection);
        if (selection.ErrorKind != null)
        {
            return EditReplacementResolution.Error(
                selection.ErrorKind.Value,
                selection.ErrorMessage!,
                selection.ErrorStrategy);
        }

        var selected = selection.Selected!;
        var selectedActualOldString = buffer.Substring(
            selected[0].Start,
            selected[0].Length);
        return BuildCandidateResolution(
            editIndex,
            requestedOldString,
            selectedActualOldString,
            actualNewString,
            replaceAll,
            strategy,
            recovered,
            selection.Notes,
            selected);
    }

    private static EditCandidateSelectionResult SelectCandidateSet(
        string buffer,
        bool replaceAll,
        int editIndex,
        EditMatchStrategy strategy,
        bool recovered,
        EditOriginMap originMap,
        EditReadCoverage readCoverage,
        EditFileConfig config,
        IReadOnlyList<EditNormalizationNote> notes,
        EditCandidateCollection collection)
    {
        var candidates = collection.Candidates;
        if (candidates.Count == 0)
        {
            return collection.SkippedGeneratedCount > 0
                ? EditCandidateSelectionResult.Error(
                    EditFileErrorKind.OverlappingMultiEdit,
                    "Later edit OldString overlaps text inserted by an earlier edit.")
                : EditCandidateSelectionResult.Error(
                    EditFileErrorKind.NoMatch,
                    $"String to replace was not found for edit {editIndex}.");
        }

        if (collection.SkippedGeneratedCount > 0)
        {
            notes = notes
                .Concat(
                    [
                        new EditNormalizationNote(
                            editIndex,
                            "generated_matches_skipped",
                            collection.SkippedGeneratedCount)
                    ])
                .ToArray();
        }

        if (recovered &&
            candidates.Count > config.MaxRecoveryCandidates)
        {
            return EditCandidateSelectionResult.Error(
                EditFileErrorKind.RecoveryLimitExceeded,
                $"Recovered match for edit {editIndex} exceeded the recovery candidate limit. Provide a more exact OldString.",
                strategy);
        }

        if (candidates.Count > config.MaxReplacementCandidates)
        {
            return EditCandidateSelectionResult.Error(
                EditFileErrorKind.ReplacementLimitExceeded,
                $"Edit {editIndex} selected at least {candidates.Count} occurrences. The maximum is {config.MaxReplacementCandidates}.",
                recovered ? strategy : null);
        }

        if (!replaceAll && candidates.Count > 1)
        {
            var kind = recovered
                ? EditFileErrorKind.RecoveryAmbiguous
                : EditFileErrorKind.AmbiguousMatch;
            return EditCandidateSelectionResult.Error(
                kind,
                $"Found {candidates.Count} matches for edit {editIndex}, but replaceAll is false.",
                recovered ? strategy : null);
        }

        if (HasOverlappingCandidates(candidates))
        {
            return recovered
                ? EditCandidateSelectionResult.Error(
                    EditFileErrorKind.RecoveryAmbiguous,
                    $"Recovered match for edit {editIndex} is ambiguous. Provide a more exact OldString.",
                    strategy)
                : EditCandidateSelectionResult.Error(
                    EditFileErrorKind.AmbiguousMatch,
                    $"Found overlapping matches for edit {editIndex}. Provide a more exact OldString.");
        }

        if (readCoverage.IsRestricted &&
            candidates.Any(candidate =>
                !originMap.IsInsideOriginalRange(
                    candidate,
                    readCoverage.StartOffset,
                    readCoverage.EndOffset)))
        {
            return EditCandidateSelectionResult.Error(
                recovered
                    ? EditFileErrorKind.RecoveryOutsideReadRange
                    : EditFileErrorKind.OutsideReadRange,
                $"Edit {editIndex} is outside the previously read range. Read the relevant lines before editing.",
                recovered ? strategy : null);
        }

        var discontinuousCount = candidates.Count(
            candidate => !originMap.HasContinuousOriginalProvenance(candidate));
        if (discontinuousCount > 0)
        {
            notes = notes
                .Concat(
                [
                    new EditNormalizationNote(
                        editIndex,
                        "source_discontinuity",
                        discontinuousCount)
                ])
                .ToArray();
        }

        if (recovered &&
            replaceAll &&
            HasMixedRecoveredCandidateShape(buffer, candidates))
        {
            return EditCandidateSelectionResult.Error(
                EditFileErrorKind.RecoverySemanticDifference,
                $"Selected recovered occurrences for edit {editIndex} have different literal representations and cannot share one replacement. Provide a more exact OldString.",
                strategy);
        }

        return new EditCandidateSelectionResult(
            replaceAll ? candidates : [candidates[0]],
            notes,
            null,
            null,
            null);
    }

    private static EditReplacementResolution BuildCandidateResolution(
        int editIndex,
        string requestedOldString,
        string actualOldString,
        string actualNewString,
        bool replaceAll,
        EditMatchStrategy strategy,
        bool recovered,
        IReadOnlyList<EditNormalizationNote> notes,
        IReadOnlyList<EditCandidate> selected)
    {
        var resolved = new ResolvedEditReplacement(
            editIndex,
            RequestedOldString: requestedOldString,
            ActualOldString: actualOldString,
            ActualNewString: actualNewString,
            ReplaceAll: replaceAll,
            MatchStrategy: strategy,
            Recovered: recovered,
            NormalizationNotes: notes)
        {
            ReplacementCount = selected.Count
        };

        return new EditReplacementResolution(
            resolved,
            selected,
            null,
            null,
            null);
    }

    private static EditReplacementResolution ToResolutionError(
        EditCandidateSelectionResult selection)
        => EditReplacementResolution.Error(
            selection.ErrorKind!.Value,
            selection.ErrorMessage!,
            selection.ErrorStrategy);

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveLineEndingNormalizedMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        EditOriginMap originMap,
        EditReadCoverage readCoverage,
        EditFileConfig config)
    {
        var normalizedBuffer = NormalizeForLineEndingMatch(buffer, out var map);
        var normalizedOldString = NormalizeForLineEndingMatch(edit.OldString, out _);
        if (string.Equals(normalizedBuffer, buffer, StringComparison.Ordinal) &&
            string.Equals(normalizedOldString, edit.OldString, StringComparison.Ordinal))
        {
            return (false, default!);
        }

        var candidates = CollectCandidates(
            EnumerateOccurrences(normalizedBuffer, normalizedOldString)
            .Select(start => new EditCandidate(map[start], map[start + normalizedOldString.Length] - map[start]))
            ,
            originMap,
            GetCandidateCollectionLimit(config, recovered: true));

        var selection = SelectCandidateSet(
            buffer,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.LineEndingNormalized,
            recovered: true,
            originMap,
            readCoverage,
            config,
            [new EditNormalizationNote(editIndex, "line_endings")],
            candidates);
        if (selection.ErrorKind != null)
            return (IsRecoverySpecificError(selection.ErrorKind), ToResolutionError(selection));

        var selected = selection.Selected!;
        var actualOld = buffer.Substring(selected[0].Start, selected[0].Length);
        var resolution = BuildCandidateResolution(
            editIndex,
            edit.OldString,
            actualOld,
            normalizedNewString,
            edit.ReplaceAll,
            EditMatchStrategy.LineEndingNormalized,
            recovered: true,
            selection.Notes,
            selected);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveBomHiddenFirstLineMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        EditOriginMap originMap,
        EditReadCoverage readCoverage,
        EditFileConfig config,
        bool fileHasBom)
    {
        if (edit.OldString.StartsWith('\uFEFF'))
            return (false, default!);

        var candidateStart = 0;
        if (buffer.StartsWith('\uFEFF'))
            candidateStart = 1;
        else if (!fileHasBom)
            return (false, default!);

        if (!buffer.AsSpan(candidateStart).StartsWith(edit.OldString.AsSpan(), StringComparison.Ordinal))
            return (false, default!);

        var candidate = new EditCandidate(candidateStart, edit.OldString.Length);
        var candidates = CollectCandidates(
            [candidate],
            originMap,
            GetCandidateCollectionLimit(config, recovered: true));
        var selection = SelectCandidateSet(
            buffer,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.BomHiddenFirstLine,
            recovered: true,
            originMap,
            readCoverage,
            config,
            [new EditNormalizationNote(editIndex, "bom_hidden_first_line")],
            candidates);
        if (selection.ErrorKind != null)
            return (IsRecoverySpecificError(selection.ErrorKind), ToResolutionError(selection));

        var selected = selection.Selected!;
        var resolution = BuildCandidateResolution(
            editIndex,
            edit.OldString,
            edit.OldString,
            normalizedNewString,
            edit.ReplaceAll,
            EditMatchStrategy.BomHiddenFirstLine,
            recovered: true,
            selection.Notes,
            selected);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveQuoteNormalizedMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        EditOriginMap originMap,
        EditReadCoverage readCoverage,
        EditFileConfig config)
    {
        var normalizedBuffer = NormalizeQuotes(buffer);
        var normalizedOldString = NormalizeQuotes(edit.OldString);
        if (normalizedBuffer == buffer && normalizedOldString == edit.OldString)
            return (false, default!);

        var candidates = CollectCandidates(
            EnumerateOccurrences(normalizedBuffer, normalizedOldString)
                .Select(start => new EditCandidate(
                    start,
                    normalizedOldString.Length)),
            originMap,
            GetCandidateCollectionLimit(config, recovered: true));
        var selection = SelectCandidateSet(
            buffer,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.QuoteNormalized,
            recovered: true,
            originMap,
            readCoverage,
            config,
            [],
            candidates);
        if (selection.ErrorKind != null)
            return (IsRecoverySpecificError(selection.ErrorKind), ToResolutionError(selection));

        var selected = selection.Selected!;
        var actualOld = buffer.Substring(selected[0].Start, selected[0].Length);
        var actualNewString = normalizedNewString;
        var notes = selection.Notes
            .Concat(
            [
                new EditNormalizationNote(
                    editIndex,
                    "quote_normalized")
            ])
            .ToArray();
        var resolution = BuildCandidateResolution(
            editIndex,
            edit.OldString,
            actualOld,
            actualNewString,
            edit.ReplaceAll,
            EditMatchStrategy.QuoteNormalized,
            recovered: true,
            notes,
            selected);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveIndentationOnlyBlockMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        EditOriginMap originMap,
        EditReadCoverage readCoverage,
        EditFileConfig config)
    {
        if (!ContainsLineBoundary(edit.OldString))
            return (false, default!);

        var requested = RemoveCommonIndentation(edit.OldString);
        var requestedNormalized = TrimTrailingNewlines(requested.NormalizedText);

        var lineCount = SplitPreservingLineEnds(edit.OldString).Length;
        var candidates = CollectCandidates(
            EnumerateLineBlockCandidates(buffer, lineCount)
                .Where(candidate =>
                {
                    if (!LineBlockMightMatchIndentation(
                            buffer,
                            candidate,
                            requestedNormalized))
                    {
                        return false;
                    }

                    var actual = buffer.Substring(candidate.Start, candidate.Length);
                    return
                        TrimTrailingNewlines(
                            RemoveCommonIndentation(actual).NormalizedText) == requestedNormalized;
                }),
            originMap,
            GetCandidateCollectionLimit(config, recovered: true));

        var selection = SelectCandidateSet(
            buffer,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.IndentationOnlyBlock,
            recovered: true,
            originMap,
            readCoverage,
            config,
            [new EditNormalizationNote(editIndex, "indentation")],
            candidates);
        if (selection.ErrorKind != null)
            return (IsRecoverySpecificError(selection.ErrorKind), ToResolutionError(selection));

        var selected = selection.Selected!;
        var actualNewString = normalizedNewString;
        var replacementRebased = false;
        if (normalizedNewString.Length > 0)
        {
            var requestedIndent = requested.Indentation;
            var candidateNewStrings = selected
                .Select(candidate =>
                {
                    var actual = buffer.Substring(candidate.Start, candidate.Length);
                    var actualIndent = RemoveCommonIndentation(actual).Indentation;
                    return RebaseCommonIndentation(
                        normalizedNewString,
                        requestedIndent,
                        actualIndent);
                })
                .ToArray();
            if (candidateNewStrings.Any(value => value == null) ||
                candidateNewStrings.Select(value => value!).Distinct(StringComparer.Ordinal).Count() != 1)
            {
                return (
                    true,
                    EditReplacementResolution.Error(
                        EditFileErrorKind.RecoverySemanticDifference,
                        $"Indentation recovery for edit {editIndex} cannot safely map the replacement indentation. Provide the exact OldString and NewString.",
                        EditMatchStrategy.IndentationOnlyBlock));
            }

            actualNewString = candidateNewStrings[0]!;
            replacementRebased = !string.Equals(
                actualNewString,
                normalizedNewString,
                StringComparison.Ordinal);
            actualNewString = PreserveBlockReplacementLineEnding(
                buffer,
                selected,
                actualNewString,
                out var lineEndingPreserved,
                out var lineEndingConflict);
            if (lineEndingConflict)
            {
                return (
                    true,
                    EditReplacementResolution.Error(
                        EditFileErrorKind.RecoverySemanticDifference,
                        $"Indentation recovery for edit {editIndex} matched incompatible line endings. Provide the exact OldString.",
                        EditMatchStrategy.IndentationOnlyBlock));
            }

            if (lineEndingPreserved)
            {
                selection = selection with
                {
                    Notes = selection.Notes
                        .Concat(
                        [
                            new EditNormalizationNote(
                                editIndex,
                                "replacement_line_ending_preserved")
                        ])
                        .ToArray()
                };
            }
        }

        var notes = replacementRebased
            ? selection.Notes
                .Concat(
                [
                    new EditNormalizationNote(
                        editIndex,
                        "replacement_indentation_rebased")
                ])
                .ToArray()
            : selection.Notes;
        var actualOld = buffer.Substring(selected[0].Start, selected[0].Length);
        var resolution = BuildCandidateResolution(
            editIndex,
            edit.OldString,
            actualOld,
            actualNewString,
            edit.ReplaceAll,
            EditMatchStrategy.IndentationOnlyBlock,
            recovered: true,
            notes,
            selected);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static bool ContainsNewOmissionPlaceholder(string oldString, string newString)
        => StandaloneEditOmissionPlaceholderRegex().IsMatch(newString) &&
           !StandaloneEditOmissionPlaceholderRegex().IsMatch(oldString);

    private static string ApplyOldBoundaryTrimmingToNewString(
        string oldString,
        string newString,
        out bool changed)
    {
        var trimmedStart = oldString.TrimStart();
        var trimmedEnd = oldString.TrimEnd();
        var leadingLength = oldString.Length - trimmedStart.Length;
        var trailingLength = oldString.Length - trimmedEnd.Length;
        var result = newString;

        if (leadingLength > 0)
        {
            var leading = oldString[..leadingLength];
            if (result.StartsWith(leading, StringComparison.Ordinal))
                result = result[leadingLength..];
        }

        if (trailingLength > 0)
        {
            var trailing = oldString[^trailingLength..];
            if (result.EndsWith(trailing, StringComparison.Ordinal))
                result = result[..^trailingLength];
        }

        changed = !string.Equals(result, newString, StringComparison.Ordinal);
        return result;
    }

    private static IEnumerable<int> EnumerateOccurrences(
        string value,
        string search)
    {
        if (search.Length == 0)
            yield break;

        var index = 0;
        while (index <= value.Length)
        {
            var found = value.IndexOf(search, index, StringComparison.Ordinal);
            if (found < 0)
                break;
            yield return found;
            index = found + 1;
        }
    }

    private static EditCandidateCollection CollectCandidates(
        IEnumerable<EditCandidate> source,
        EditOriginMap originMap,
        int limit)
    {
        var candidates = new List<EditCandidate>();
        long skippedGenerated = 0;
        EditCandidate? previous = null;
        foreach (var candidate in source)
        {
            if (previous == candidate)
                continue;
            previous = candidate;

            if (originMap.IntersectsGeneratedContent(candidate))
            {
                skippedGenerated++;
                continue;
            }

            candidates.Add(candidate);
            if (candidates.Count > limit)
                break;
        }

        return new EditCandidateCollection(
            candidates,
            checked((int)Math.Min(
                skippedGenerated,
                int.MaxValue)));
    }

    private static int GetCandidateCollectionLimit(
        EditFileConfig config,
        bool recovered)
        => recovered
            ? Math.Min(
                config.MaxRecoveryCandidates,
                config.MaxReplacementCandidates)
            : config.MaxReplacementCandidates;

    private static EditReadCoverage GetEditReadCoverage(
        string originalText,
        ReadFileSnapshot? priorRead)
    {
        if (priorRead == null ||
            priorRead.Coverage is ReadFileCoverage.FullFile or ReadFileCoverage.EmptyFile)
        {
            return EditReadCoverage.Unrestricted;
        }

        if (priorRead.Coverage == ReadFileCoverage.Truncated)
            return EditReadCoverage.Blocked;

        var lineStarts = GetLineStarts(originalText);
        var coveredEndLine = Math.Max(
            priorRead.EndLine,
            priorRead.StartLine + Math.Max(1, priorRead.LinesRead) - 1);
        if (priorRead.StartLine < 1 ||
            coveredEndLine < priorRead.StartLine ||
            priorRead.StartLine > lineStarts.Count)
        {
            return EditReadCoverage.Blocked;
        }

        var startOffset = lineStarts[priorRead.StartLine - 1];
        var endOffset = coveredEndLine < lineStarts.Count
            ? lineStarts[coveredEndLine]
            : originalText.Length;
        return new EditReadCoverage(
            IsRestricted: true,
            startOffset,
            endOffset);
    }

    private static bool HasOverlappingCandidates(IReadOnlyList<EditCandidate> candidates)
    {
        var ordered = candidates.OrderBy(candidate => candidate.Start).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].Start < ordered[i - 1].Start + ordered[i - 1].Length)
                return true;
        }

        return false;
    }

    private static bool HasMixedRecoveredCandidateShape(string buffer, IReadOnlyList<EditCandidate> candidates)
    {
        if (candidates.Count <= 1)
            return false;

        var first = buffer.Substring(candidates[0].Start, candidates[0].Length);
        return candidates
            .Skip(1)
            .Any(candidate => !string.Equals(buffer.Substring(candidate.Start, candidate.Length), first, StringComparison.Ordinal));
    }

    private static bool IsRecoverySpecificError(EditFileErrorKind? kind)
        => kind is EditFileErrorKind.RecoveryAmbiguous or
            EditFileErrorKind.RecoveryLimitExceeded or
            EditFileErrorKind.RecoveryOutsideReadRange or
            EditFileErrorKind.RecoverySemanticDifference or
            EditFileErrorKind.ReplacementLimitExceeded or
            EditFileErrorKind.OverlappingMultiEdit;

    private static bool IsTerminalCandidateError(EditFileErrorKind? kind)
        => kind is EditFileErrorKind.AmbiguousMatch or
            EditFileErrorKind.OutsideReadRange or
            EditFileErrorKind.RecoveryOutsideReadRange or
            EditFileErrorKind.OverlappingMultiEdit or
            EditFileErrorKind.ReplacementLimitExceeded;

    private static IEnumerable<EditCandidate> EnumerateDeleteLineCandidates(
        string buffer,
        string oldString)
    {
        foreach (var start in EnumerateOccurrences(buffer, oldString))
        {
            var matchEnd = start + oldString.Length;
            var startsAtLineStart =
                start == 0 ||
                buffer[start - 1] is '\r' or '\n';
            var endsAtLineEnd =
                matchEnd == buffer.Length ||
                buffer[matchEnd] is '\r' or '\n';
            if (startsAtLineStart && endsAtLineEnd)
            {
                var lineEndingLength =
                    matchEnd < buffer.Length &&
                    buffer[matchEnd] == '\r' &&
                    matchEnd + 1 < buffer.Length &&
                    buffer[matchEnd + 1] == '\n'
                        ? 2
                        : matchEnd < buffer.Length ? 1 : 0;
                yield return new EditCandidate(
                    start,
                    oldString.Length + lineEndingLength);
                continue;
            }

            yield return new EditCandidate(start, oldString.Length);
        }
    }

    private static bool EndsWithAnyNewline(string value)
        => value.EndsWith('\n') || value.EndsWith('\r');

    private static string TrimTrailingNewlines(string value)
        => value.TrimEnd('\r', '\n');

    private static string NormalizeForLineEndingMatch(string value, out int[] map)
    {
        var normalized = new StringBuilder(value.Length);
        var offsets = new List<int> { 0 };
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\r')
            {
                normalized.Append('\n');
                if (i + 1 < value.Length && value[i + 1] == '\n')
                {
                    offsets.Add(i + 2);
                    i++;
                }
                else
                {
                    offsets.Add(i + 1);
                }

                continue;
            }

            normalized.Append(value[i]);
            offsets.Add(i + 1);
        }

        map = offsets.ToArray();
        return normalized.ToString();
    }

    private static bool TryDesanitize(
        string value,
        IReadOnlyDictionary<string, string> replacements,
        out string desanitized,
        out string kind)
    {
        desanitized = value;
        foreach (var (from, to) in replacements)
            desanitized = desanitized.Replace(from, to, StringComparison.Ordinal);

        kind = "desanitized";
        return desanitized != value;
    }

    private static string NormalizeQuotes(string value)
        => value
            .Replace('‘', '\'')
            .Replace('’', '\'')
            .Replace('“', '"')
            .Replace('”', '"');

    private static string UnescapeSearchString(string value, out bool changed)
    {
        var builder = new StringBuilder(value.Length);
        changed = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            var replacement = value[i + 1] switch
            {
                'n' => "\n",
                't' => "\t",
                'r' => "\r",
                '\'' => "'",
                '"' => "\"",
                '`' => "`",
                '\\' => "\\",
                '$' => "$",
                _ => null
            };

            if (replacement == null)
            {
                builder.Append(value[i]);
                continue;
            }

            builder.Append(replacement);
            changed = true;
            i++;
        }

        return builder.ToString();
    }

    private static CommonIndentationResult RemoveCommonIndentation(string text)
    {
        var lines = SplitPreservingLineEnds(text);
        var nonEmptyLines = lines
            .Select(RemoveLineEnding)
            .Where(line => line.Trim().Length > 0)
            .ToArray();

        if (nonEmptyLines.Length == 0)
            return new CommonIndentationResult(string.Empty, text);

        var indentation = nonEmptyLines
            .Select(GetLeadingWhitespace)
            .Aggregate((current, next) => CommonPrefix(current, next));

        if (indentation.Length == 0)
            return new CommonIndentationResult(string.Empty, text);

        var normalized = string.Concat(lines.Select(line =>
        {
            var lineEnding = GetLineEnding(line);
            var body = RemoveLineEnding(line);
            return body.StartsWith(indentation, StringComparison.Ordinal) && body.Trim().Length > 0
                ? body[indentation.Length..] + lineEnding
                : line;
        }));

        return new CommonIndentationResult(indentation, normalized);
    }

    private static bool LineBlockMightMatchIndentation(
        string buffer,
        EditCandidate candidate,
        string requestedNormalized)
    {
        var candidateLine = FindFirstNonWhitespaceLine(
            buffer,
            candidate.Start,
            candidate.Start + candidate.Length);
        var requestedLine = FindFirstNonWhitespaceLine(
            requestedNormalized,
            0,
            requestedNormalized.Length);
        if (candidateLine == null || requestedLine == null)
            return candidateLine == null && requestedLine == null;

        return string.Equals(
            candidateLine.TrimStart(),
            requestedLine.TrimStart(),
            StringComparison.Ordinal);
    }

    private static string? FindFirstNonWhitespaceLine(
        string text,
        int start,
        int end)
    {
        var lineStart = start;
        while (lineStart < end)
        {
            var lineEnd = lineStart;
            while (lineEnd < end &&
                   text[lineEnd] is not '\r' and not '\n')
            {
                lineEnd++;
            }

            var line = text.AsSpan(lineStart, lineEnd - lineStart);
            if (!line.Trim().IsEmpty)
                return line.ToString();

            if (lineEnd == end)
                break;

            if (text[lineEnd] == '\r' &&
                lineEnd + 1 < end &&
                text[lineEnd + 1] == '\n')
            {
                lineEnd++;
            }

            lineStart = lineEnd + 1;
        }

        return null;
    }

    private static string ApplyReplacementsToBuffer(
        string source,
        IReadOnlyList<EditCandidate> orderedRanges,
        string replacement)
    {
        var builder = new StringBuilder(source.Length);
        var sourcePosition = 0;
        foreach (var range in orderedRanges)
        {
            builder.Append(
                source,
                sourcePosition,
                range.Start - sourcePosition);
            builder.Append(replacement);
            sourcePosition = range.Start + range.Length;
        }

        builder.Append(
            source,
            sourcePosition,
            source.Length - sourcePosition);
        return builder.ToString();
    }

    private static string? RebaseCommonIndentation(
        string newString,
        string requestedIndent,
        string actualIndent)
    {
        if (newString.Length == 0)
            return string.Empty;

        var newIndent = RemoveCommonIndentation(newString).Indentation;
        if (!string.Equals(newIndent, requestedIndent, StringComparison.Ordinal))
            return null;

        var lines = SplitPreservingLineEnds(newString);
        return string.Concat(lines.Select(line =>
        {
            var lineEnding = GetLineEnding(line);
            var body = RemoveLineEnding(line);
            if (body.Trim().Length == 0)
                return line;

            return actualIndent +
                   body[requestedIndent.Length..] +
                   lineEnding;
        }));
    }

    private static string PreserveBlockReplacementLineEnding(
        string buffer,
        IReadOnlyList<EditCandidate> selected,
        string newString,
        out bool preserved,
        out bool conflict)
    {
        preserved = false;
        conflict = false;
        if (newString.Length == 0 || EndsWithAnyNewline(newString))
            return newString;

        var endings = selected
            .Select(candidate =>
            {
                var end = candidate.Start + candidate.Length;
                if (end == candidate.Start || end > buffer.Length)
                    return string.Empty;
                if (buffer[end - 1] == '\n')
                {
                    return end >= 2 && buffer[end - 2] == '\r'
                        ? "\r\n"
                        : "\n";
                }

                return buffer[end - 1] == '\r'
                    ? "\r"
                    : string.Empty;
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (endings.Length != 1)
        {
            conflict = endings.Length > 1;
            return newString;
        }

        if (endings[0].Length == 0)
            return newString;

        preserved = true;
        return newString + endings[0];
    }

    private static IEnumerable<EditCandidate> EnumerateLineBlockCandidates(string buffer, int lineCount)
    {
        var lineStarts = GetLineStarts(buffer);
        for (var i = 0; i < lineStarts.Count; i++)
        {
            var endLineIndex = i + lineCount;
            if (endLineIndex > lineStarts.Count)
                yield break;

            var start = lineStarts[i];
            var end = endLineIndex < lineStarts.Count ? lineStarts[endLineIndex] : buffer.Length;
            yield return new EditCandidate(start, end - start);
        }
    }

    private static IReadOnlyList<int> GetLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' &&
                i + 1 < text.Length &&
                text[i + 1] == '\n')
            {
                i++;
                if (i + 1 < text.Length)
                    starts.Add(i + 1);
                continue;
            }

            if (text[i] is '\r' or '\n' &&
                i + 1 < text.Length)
            {
                starts.Add(i + 1);
            }
        }

        return starts;
    }

    private static string[] SplitPreservingLineEnds(string text)
    {
        if (text.Length == 0)
            return [string.Empty];

        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not '\r' and not '\n')
                continue;

            if (text[i] == '\r' &&
                i + 1 < text.Length &&
                text[i + 1] == '\n')
            {
                i++;
            }

            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);

        return lines.ToArray();
    }

    private static bool ContainsLineBoundary(string text)
        => text.Contains('\r', StringComparison.Ordinal) ||
           text.Contains('\n', StringComparison.Ordinal);

    private static string RemoveLineEnding(string line)
        => line.EndsWith("\r\n", StringComparison.Ordinal)
            ? line[..^2]
            : line.EndsWith('\n') || line.EndsWith('\r')
                ? line[..^1]
                : line;

    private static string GetLineEnding(string line)
        => line.EndsWith("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : line.EndsWith('\n')
                ? "\n"
                : line.EndsWith('\r')
                    ? "\r"
                    : string.Empty;

    private static string GetLeadingWhitespace(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]) && line[index] is not '\r' and not '\n')
            index++;
        return line[..index];
    }

    private static string CommonPrefix(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index])
            index++;
        return left[..index];
    }

    private static string ComputeEditReturnedContentHash(string text)
    {
        using var reader = new StringReader(text);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
            lines.Add(line);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static FileEditAppliedEvent BuildFileEditAppliedEvent(FileMutationEventBuildRequest request, EditApplicationResult application)
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
            EditCount = application.ResolvedReplacements.Count,
            ReplacementCount = application.ReplacementCount,
            Replacements = application.ResolvedReplacements
                .Select(replacement => new FileEditAppliedReplacement(
                    replacement.Index,
                    replacement.ReplaceAll,
                    replacement.ReplacementCount,
                    FormatEnum(replacement.MatchStrategy),
                    replacement.Recovered,
                    ComputeContentHash(replacement.RequestedOldString),
                    ComputeContentHash(replacement.ActualOldString),
                    replacement.RequestedOldString.Length,
                    replacement.ActualOldString.Length,
                    application.TextEdits
                        .Where(edit => edit.EditIndex == replacement.Index)
                        .Select(edit => edit.BeforeRange)
                        .ToArray(),
                    application.TextEdits
                        .Where(edit => edit.EditIndex == replacement.Index)
                        .Select(edit => edit.AfterRange)
                        .ToArray()))
                .ToArray(),
            Normalizations = application.NormalizationNotes
                .Select(note => new FileEditNormalizationNote(
                    note.EditIndex,
                    note.Kind,
                    note.Count))
                .ToArray()
        };

    private static string FormatEditResult(FileMutationResult result, EditApplicationResult application)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);
        writer.WriteStartElement("edit_file");
        writer.WriteAttributeString("path", result.Path);
        writer.WriteAttributeString("edits", application.ResolvedReplacements.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("replacements", application.ReplacementCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("changed", result.Changed.ToString().ToLowerInvariant());
        writer.WriteAttributeString("created", result.Created.ToString().ToLowerInvariant());
        writer.WriteAttributeString("replace_all_count", application.ResolvedReplacements.Count(replacement => replacement.ReplaceAll).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("content_hash", result.ContentHash);
        writer.WriteAttributeString("byte_length", result.ByteLength.ToString(CultureInfo.InvariantCulture));
        if (result.FirstChangedLine.HasValue)
            writer.WriteAttributeString("first_changed_line", result.FirstChangedLine.Value.ToString(CultureInfo.InvariantCulture));
        if (result.LastChangedLine.HasValue)
            writer.WriteAttributeString("last_changed_line", result.LastChangedLine.Value.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("event_emitted", result.EventEmitted.ToString().ToLowerInvariant());

        foreach (var replacement in application.ResolvedReplacements)
        {
            writer.WriteStartElement("match_strategy");
            writer.WriteAttributeString("edit", replacement.Index.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("kind", FormatEnum(replacement.MatchStrategy));
            writer.WriteAttributeString("recovered", replacement.Recovered.ToString().ToLowerInvariant());
            writer.WriteEndElement();
        }

        foreach (var note in application.NormalizationNotes)
        {
            writer.WriteStartElement("normalization");
            writer.WriteAttributeString("edit", note.EditIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("kind", note.Kind);
            if (note.Count.HasValue)
                writer.WriteAttributeString("count", note.Count.Value.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static string FormatEditError(EditFileErrorKind kind, string? path, string message, EditMatchStrategy? strategy = null)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);
        writer.WriteStartElement("error");
        writer.WriteAttributeString("tool", "EditFile");
        writer.WriteAttributeString("kind", FormatEnum(kind));
        if (strategy.HasValue)
            writer.WriteAttributeString("strategy", FormatEnum(strategy.Value));
        if (!string.IsNullOrWhiteSpace(path))
            writer.WriteAttributeString("path", path);
        writer.WriteString(message);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    [GeneratedRegex(@"(?m)^\s*(?:(?://\s*)?\((?:rest of (?:methods|code)|unchanged code) \.\.\.\)|//\s*(?:rest of (?:methods|code)|unchanged code) \.\.\.)\s*$")]
    private static partial Regex StandaloneEditOmissionPlaceholderRegex();
}

/// <summary>Describes one exact text replacement applied by <see cref="CodingToolHarness.EditFile"/>.</summary>
/// <param name="OldString">The existing text to locate.</param>
/// <param name="NewString">The complete replacement text.</param>
/// <param name="ReplaceAll">Whether every occurrence may be replaced.</param>
public sealed record FileEditReplacement(
    [property: Description("The exact existing text to replace. Do not include ReadFile line-number prefixes.")]
    string OldString,
    [property: Description("The complete replacement text.")]
    string NewString,
    [property: Description("Whether to replace every occurrence. Omit or use false when oldString must identify one unique occurrence.")]
    bool ReplaceAll = false);

internal sealed record EditReadValidationResult(
    ReadFileSnapshot? PriorRead,
    EditFileErrorKind? ErrorKind,
    string? ErrorMessage);

internal sealed record EditApplicationResult(
    string OriginalContent,
    string UpdatedContent,
    IReadOnlyList<FileMutationTextEdit> TextEdits,
    IReadOnlyList<ResolvedEditReplacement> ResolvedReplacements,
    int ReplacementCount,
    bool Created,
    IReadOnlyList<EditNormalizationNote> NormalizationNotes)
{
    public EditFileErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }
    public EditMatchStrategy? ErrorStrategy { get; init; }

    public static EditApplicationResult Error(EditFileErrorKind kind, string message, EditMatchStrategy? strategy = null)
        => new(
            string.Empty,
            string.Empty,
            [],
            [],
            0,
            Created: false,
            [])
        {
            ErrorKind = kind,
            ErrorMessage = message,
            ErrorStrategy = strategy
        };
}

internal sealed record EditReplacementResolution(
    ResolvedEditReplacement? Resolved,
    IReadOnlyList<EditCandidate>? Ranges,
    EditFileErrorKind? ErrorKind,
    string? ErrorMessage,
    EditMatchStrategy? ErrorStrategy)
{
    public static EditReplacementResolution Error(EditFileErrorKind kind, string message, EditMatchStrategy? strategy = null)
        => new(null, null, kind, message, strategy);
}

internal sealed record EditCandidateSelectionResult(
    IReadOnlyList<EditCandidate>? Selected,
    IReadOnlyList<EditNormalizationNote> Notes,
    EditFileErrorKind? ErrorKind,
    string? ErrorMessage,
    EditMatchStrategy? ErrorStrategy)
{
    public static EditCandidateSelectionResult Error(
        EditFileErrorKind kind,
        string message,
        EditMatchStrategy? strategy = null)
        => new(null, [], kind, message, strategy);
}

internal sealed record EditCandidateCollection(
    IReadOnlyList<EditCandidate> Candidates,
    int SkippedGeneratedCount);

internal sealed record ResolvedEditReplacement(
    int Index,
    string RequestedOldString,
    string ActualOldString,
    string ActualNewString,
    bool ReplaceAll,
    EditMatchStrategy MatchStrategy,
    bool Recovered,
    IReadOnlyList<EditNormalizationNote> NormalizationNotes)
{
    public int ReplacementCount { get; init; } = 1;
}

internal sealed record EditNormalizationNote(
    int EditIndex,
    string Kind,
    int? Count = null);

internal readonly record struct EditCandidate(int Start, int Length);

internal readonly record struct EditReadCoverage(
    bool IsRestricted,
    int StartOffset,
    int EndOffset)
{
    public static EditReadCoverage Unrestricted { get; } = new(false, 0, 0);
    public static EditReadCoverage Blocked { get; } = new(true, 0, 0);
}

internal sealed record EditOriginMapApplyResult(
    EditOriginMap? Map,
    EditOriginMapApplyStatus Status);

internal enum EditOriginMapApplyStatus
{
    Success,
    InvalidRange,
    CoverageGap
}

internal sealed class EditOriginMap
{
    private readonly IReadOnlyList<EditOriginSegment> _segments;
    private readonly int _length;

    private EditOriginMap(
        IReadOnlyList<EditOriginSegment> segments,
        int length)
    {
        _segments = segments;
        _length = length;
    }

    public static EditOriginMap Create(int length)
    {
        if (length == 0)
            return new EditOriginMap([], 0);

        return new EditOriginMap(
            [new EditOriginSegment(0, length, 0)],
            length);
    }

    public bool IntersectsGeneratedContent(EditCandidate candidate)
    {
        var candidateEnd = candidate.Start + candidate.Length;
        var segmentIndex = FindFirstOverlappingSegment(
            candidate.Start);
        for (var i = segmentIndex; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.Start >= candidateEnd)
                break;
            if (segment.OriginalStart == null &&
                segment.Start + segment.Length > candidate.Start)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsInsideOriginalRange(
        EditCandidate candidate,
        int startOffset,
        int endOffset)
    {
        if (candidate.Length == 0 ||
            startOffset < 0 ||
            endOffset <= startOffset ||
            candidate.Start < 0 ||
            candidate.Start + candidate.Length > _length)
        {
            return false;
        }

        var coveredLength = 0;
        var candidateEnd = candidate.Start + candidate.Length;
        var segmentIndex = FindFirstOverlappingSegment(
            candidate.Start);
        for (var i = segmentIndex; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.Start >= candidateEnd)
                break;

            var intersectionStart = Math.Max(candidate.Start, segment.Start);
            var intersectionEnd = Math.Min(
                candidateEnd,
                segment.Start + segment.Length);
            if (intersectionEnd <= intersectionStart)
                continue;

            if (segment.OriginalStart == null)
                return false;

            var originalOffset =
                segment.OriginalStart.Value +
                intersectionStart -
                segment.Start;
            var intersectionLength = intersectionEnd - intersectionStart;
            if (originalOffset < startOffset ||
                originalOffset + intersectionLength > endOffset)
            {
                return false;
            }

            coveredLength += intersectionLength;
        }

        return coveredLength == candidate.Length;
    }

    public bool HasContinuousOriginalProvenance(EditCandidate candidate)
    {
        if (candidate.Length <= 0)
            return true;

        var candidateEnd = candidate.Start + candidate.Length;
        var segmentIndex = FindFirstOverlappingSegment(
            candidate.Start);
        int? expectedOriginalOffset = null;
        for (var i = segmentIndex; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.Start >= candidateEnd)
                break;

            var intersectionStart = Math.Max(candidate.Start, segment.Start);
            var intersectionEnd = Math.Min(
                candidateEnd,
                segment.Start + segment.Length);
            if (intersectionEnd <= intersectionStart)
                continue;
            if (segment.OriginalStart == null)
                return false;

            var originalStart =
                segment.OriginalStart.Value +
                intersectionStart -
                segment.Start;
            if (expectedOriginalOffset.HasValue &&
                originalStart != expectedOriginalOffset.Value)
            {
                return false;
            }

            expectedOriginalOffset =
                originalStart + intersectionEnd - intersectionStart;
        }

        return true;
    }

    public EditOriginMapApplyResult ApplyReplacements(
        IReadOnlyList<EditCandidate> orderedRanges,
        int replacementLength)
    {
        var validatedCursor = 0;
        foreach (var range in orderedRanges)
        {
            if (range.Start < validatedCursor ||
                range.Length <= 0 ||
                range.Start + range.Length > _length)
            {
                return new EditOriginMapApplyResult(
                    null,
                    EditOriginMapApplyStatus.InvalidRange);
            }

            validatedCursor = range.Start + range.Length;
        }

        var nextSegments = new List<EditOriginSegment>();
        var sourcePosition = 0;
        var segmentIndex = 0;
        var segmentOffset = 0;
        var newCursor = 0;
        var coverageGap = false;

        void AppendSegment(
            int length,
            int? originalStart)
        {
            if (length == 0)
                return;

            if (nextSegments.Count > 0)
            {
                var previous = nextSegments[^1];
                var previousEnd = previous.Start + previous.Length;
                var originContinues =
                    previous.OriginalStart.HasValue &&
                    originalStart == previous.OriginalStart.Value + previous.Length;
                var generatedContinues =
                    previous.OriginalStart == null &&
                    originalStart == null;
                if (previousEnd == newCursor &&
                    (originContinues || generatedContinues))
                {
                    nextSegments[^1] = previous with
                    {
                        Length = previous.Length + length
                    };
                    newCursor += length;
                    return;
                }
            }

            nextSegments.Add(new EditOriginSegment(
                newCursor,
                length,
                originalStart));
            newCursor += length;
        }

        void ConsumeTo(
            int end,
            bool copy)
        {
            if (coverageGap || end <= sourcePosition)
                return;

            while (sourcePosition < end)
            {
                if (segmentIndex >= _segments.Count)
                {
                    coverageGap = true;
                    return;
                }

                var segment = _segments[segmentIndex];
                var length = Math.Min(
                    end - sourcePosition,
                    segment.Length - segmentOffset);
                int? originalStart = segment.OriginalStart == null
                    ? null
                    : segment.OriginalStart.Value + segmentOffset;
                if (copy)
                    AppendSegment(length, originalStart);

                sourcePosition += length;
                segmentOffset += length;
                if (segmentOffset == segment.Length)
                {
                    segmentIndex++;
                    segmentOffset = 0;
                }
            }
        }

        foreach (var range in orderedRanges)
        {
            ConsumeTo(range.Start, copy: true);
            ConsumeTo(range.Start + range.Length, copy: false);
            AppendSegment(replacementLength, originalStart: null);
        }

        ConsumeTo(_length, copy: true);
        return coverageGap
            ? new EditOriginMapApplyResult(
                null,
                EditOriginMapApplyStatus.CoverageGap)
            : new EditOriginMapApplyResult(
                new EditOriginMap(nextSegments, newCursor),
                EditOriginMapApplyStatus.Success);
    }

    private int FindFirstOverlappingSegment(int start)
    {
        var low = 0;
        var high = _segments.Count - 1;
        var result = _segments.Count;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var segment = _segments[middle];
            if (segment.Start + segment.Length <= start)
            {
                low = middle + 1;
                continue;
            }

            result = middle;
            high = middle - 1;
        }

        return result;
    }
}

internal sealed record EditOriginSegment(
    int Start,
    int Length,
    int? OriginalStart);

internal sealed record CommonIndentationResult(
    string Indentation,
    string NormalizedText);

internal enum EditMatchStrategy
{
    Create,
    Exact,
    LineEndingNormalized,
    BomHiddenFirstLine,
    Desanitized,
    QuoteNormalized,
    EscapedStringNormalized,
    TrimmedBoundary,
    IndentationOnlyBlock
}

internal enum EditFileErrorKind
{
    InvalidArguments,
    InternalError,
    NotRead,
    HistoryReducedRead,
    StaleRead,
    OutsideReadRange,
    NoMatch,
    AmbiguousMatch,
    NoChange,
    InvalidEmptyOldString,
    OverlappingMultiEdit,
    NewOmissionPlaceholder,
    RecoveryAmbiguous,
    RecoveryLimitExceeded,
    ReplacementLimitExceeded,
    RecoveryOutsideReadRange,
    RecoverySemanticDifference
}

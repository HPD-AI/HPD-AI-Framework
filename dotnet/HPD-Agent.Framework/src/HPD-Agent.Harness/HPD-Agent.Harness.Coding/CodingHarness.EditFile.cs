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
    private const int MaxEditCount = 20;
    private const int MaxNormalizationNotes = 50;
    private const int MinRecoveryOldStringChars = 10;
    private const int MaxRecoveryCandidates = 20;

    [AIFunction]
    [RequiresPermission]
    [Description("Edits a text file by applying one or more targeted string replacements. Use after ReadFile has shown the relevant content. The oldString values should match file content exactly and uniquely unless replaceAll is true; the tool may recover from mechanical mismatches such as line endings, escaping, quotes, indentation, or whitespace when the target is still unique and inside the read range. Use WriteFile for whole-file rewrites, Grep to find unknown text, and ReadFile to inspect context before editing. Do not include ReadFile line-number prefixes in oldString or newString.")]
    public async Task<string> EditFile(
        [Description("The file path to edit. Relative paths are resolved from the current working directory.")] string path,
        [Description("One or more targeted replacements to apply sequentially.")] IReadOnlyList<FileEditReplacement> edits,
        FunctionExecutionContext context)
    {
        ResolvedMutationPath? resolvedPath = null;
        try
        {
            var argumentError = ValidateEditArguments(path, edits);
            if (argumentError != null)
                return FormatEditError(EditFileErrorKind.InvalidArguments, path, argumentError);

            resolvedPath = ResolveMutationPath(path);
            ValidateMutationPath(resolvedPath);

            var allowMissing = IsCreateEdit(edits);
            var current = await ReadMutationContentAsync(resolvedPath.FullPath, allowMissing).ConfigureAwait(false);
            if (current.Exists && current.ByteLength > 0 && allowMissing)
            {
                return FormatEditError(
                    EditFileErrorKind.InvalidEmptyOldString,
                    resolvedPath.FullPath,
                    "Cannot edit a non-empty file with an empty OldString.");
            }

            var priorRead = ValidateEditReadPolicy(resolvedPath.FullPath, current, allowMissing, context);
            if (priorRead.ErrorKind != null)
                return FormatEditError(priorRead.ErrorKind.Value, resolvedPath.FullPath, priorRead.ErrorMessage!);

            var application = ApplyEdits(resolvedPath.FullPath, current, edits, priorRead.PriorRead);
            if (application.ErrorKind != null)
                return FormatEditError(application.ErrorKind.Value, resolvedPath.FullPath, application.ErrorMessage!, application.ErrorStrategy);

            var mutationKind = application.Created ? CodingFileMutationKind.Created : CodingFileMutationKind.Changed;
            var result = await ApplyTextMutationAsync(
                new FileMutationRequest(
                    ToolName: "EditFile",
                    Path: resolvedPath.FullPath,
                    UpdatedText: application.UpdatedContent,
                    Kind: mutationKind,
                    AllowCreate: application.Created,
                    CreateParentDirectories: application.Created,
                    NormalizeLineEndingsToExistingFile: current.Exists,
                    TextEdits: application.TextEdits,
                    Notes: application.NormalizationNotes
                        .Take(MaxNormalizationNotes)
                        .Select(note => new FileMutationNote(note.EditIndex, note.Kind))
                        .ToArray(),
                    FunctionContext: context,
                    ValidateBeforeMutation: before => ValidateEditBeforeMutation(resolvedPath.FullPath, priorRead.PriorRead, before, application.Created),
                    EventFactory: request => BuildFileEditAppliedEvent(request, application))).ConfigureAwait(false);

            return FormatEditResult(result, application);
        }
        catch (FileMutationException exception) when (exception.Kind == FileMutationErrorKind.StaleFile)
        {
            return FormatEditError(
                EditFileErrorKind.StaleRead,
                resolvedPath?.FullPath ?? path,
                "File has been modified since it was read. Read it again before editing.");
        }
        catch (FileMutationException exception)
        {
            return FormatMutationError("EditFile", exception.Kind, resolvedPath?.FullPath ?? path, exception.Message);
        }
        catch (DecoderFallbackException)
        {
            return FormatMutationError("EditFile", FileMutationErrorKind.DecodeError, resolvedPath?.FullPath ?? path, "Unable to decode file as text.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FormatMutationError("EditFile", FileMutationErrorKind.WriteFailed, resolvedPath?.FullPath ?? path, $"Unable to edit file: {exception.Message}");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FormatEditError(EditFileErrorKind.InvalidArguments, path, $"Invalid path: {exception.Message}");
        }
    }

    private static string? ValidateEditArguments(string? path, IReadOnlyList<FileEditReplacement>? edits)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Path is required.";

        if (edits == null)
            return "At least one edit is required.";

        if (edits.Count == 0)
            return "At least one edit is required.";

        if (edits.Count > MaxEditCount)
            return $"Too many edits. Maximum is {MaxEditCount}.";

        for (var i = 0; i < edits.Count; i++)
        {
            var editNumber = i + 1;
            if (edits[i].OldString == null)
                return $"OldString is required for edit {editNumber} unless creating or filling an empty file.";

            if (edits[i].NewString == null)
                return $"NewString is required for edit {editNumber}.";

            if (edits[i].OldString == edits[i].NewString)
                return $"OldString and NewString must be different for edit {editNumber}.";
        }

        return null;
    }

    private static bool IsCreateEdit(IReadOnlyList<FileEditReplacement> edits)
        => edits.Count == 1 && edits[0].OldString.Length == 0;

    private static EditReadValidationResult ValidateEditReadPolicy(
        string fullPath,
        FileMutationContent current,
        bool allowMissingCreate,
        FunctionExecutionContext context)
    {
        if (!current.Exists)
            return allowMissingCreate
                ? new EditReadValidationResult(null, null, null)
                : new EditReadValidationResult(null, EditFileErrorKind.NoMatch, "String to replace was not found for edit 1.");

        if (current.ByteLength == 0)
            return new EditReadValidationResult(null, null, null);

        var readFileStateKey = typeof(ReadFileState).FullName!;
        var priorRead = context
            .Analyze(state => state.MiddlewareState.GetState<ReadFileState>(readFileStateKey))
            ?.FilesByPath.GetValueOrDefault(fullPath);

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

    private static void ValidateEditBeforeMutation(
        string fullPath,
        ReadFileSnapshot? priorRead,
        FileMutationContent current,
        bool creating)
    {
        if (creating || !current.Exists || current.ByteLength == 0)
            return;

        if (priorRead == null)
            throw new FileMutationException(FileMutationErrorKind.StaleFile, "File has not been read yet.");

        if (priorRead.Path != fullPath)
            throw new FileMutationException(FileMutationErrorKind.StaleFile, "File has been modified since it was read.");

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
        string fullPath,
        FileMutationContent current,
        IReadOnlyList<FileEditReplacement> edits,
        ReadFileSnapshot? priorRead)
    {
        if (edits.Any(edit => edit.OldString.Length == 0) && !IsCreateEdit(edits))
        {
            return EditApplicationResult.Error(
                EditFileErrorKind.InvalidEmptyOldString,
                "OldString is required for edit 1 unless creating or filling an empty file.");
        }

        if (!current.Exists)
        {
            if (!IsCreateEdit(edits))
                return EditApplicationResult.Error(EditFileErrorKind.NoMatch, "String to replace was not found for edit 1.");

            var newText = NormalizeEditNewString(fullPath, edits[0].NewString, normalizeTrailingWhitespace: false, out var createNotes, 1);
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
            var newText = NormalizeEditNewString(fullPath, edits[0].NewString, normalizeTrailingWhitespace: false, out var fillNotes, 1);
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
        var textEdits = new List<FileMutationTextEdit>();
        var resolvedReplacements = new List<ResolvedEditReplacement>();
        var normalizationNotes = new List<EditNormalizationNote>();
        var insertedTexts = new List<string>();
        var replacementCount = 0;

        for (var i = 0; i < edits.Count; i++)
        {
            var editIndex = i + 1;
            var edit = edits[i];
            var oldWithoutTrailingNewlines = TrimTrailingNewlines(edit.OldString);
            if (oldWithoutTrailingNewlines.Length > 0 &&
                insertedTexts.Any(inserted => inserted.Contains(oldWithoutTrailingNewlines, StringComparison.Ordinal)))
            {
                return EditApplicationResult.Error(
                    EditFileErrorKind.OverlappingMultiEdit,
                    "Later edit OldString overlaps text inserted by an earlier edit.");
            }

            if (ContainsNewOmissionPlaceholder(edit.OldString, edit.NewString))
            {
                return EditApplicationResult.Error(
                    EditFileErrorKind.NewOmissionPlaceholder,
                    $"NewString for edit {editIndex} contains an omission placeholder. Provide exact literal replacement text.");
            }

            var normalizedNewString = NormalizeEditNewString(fullPath, edit.NewString, normalizeTrailingWhitespace: true, out var editNotes, editIndex);
            var resolution = ResolveReplacement(buffer, edit, normalizedNewString, editIndex, priorRead, current.HasBom);
            if (resolution.ErrorKind != null)
                return EditApplicationResult.Error(resolution.ErrorKind.Value, resolution.ErrorMessage!, resolution.ErrorStrategy);

            var resolved = resolution.Resolved!;
            var ranges = resolution.Ranges!;
            var beforeBuffer = buffer;
            if (!EndsWithAnyNewline(beforeBuffer) &&
                EndsWithAnyNewline(resolved.ActualNewString) &&
                ranges.Any(range => range.Start + range.Length == beforeBuffer.Length))
            {
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

            foreach (var range in Enumerable.Reverse(orderedRanges))
                buffer = buffer.Remove(range.Start, range.Length).Insert(range.Start, resolved.ActualNewString);

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
            normalizationNotes.AddRange(editNotes);
            normalizationNotes.AddRange(resolved.NormalizationNotes);
            insertedTexts.Add(resolved.ActualNewString);
            replacementCount += ranges.Count;
        }

        return new EditApplicationResult(
            current.Text,
            buffer,
            textEdits,
            resolvedReplacements,
            replacementCount,
            Created: false,
            normalizationNotes.Take(MaxNormalizationNotes).ToArray());
    }

    private static EditReplacementResolution ResolveReplacement(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        ReadFileSnapshot? priorRead,
        bool fileHasBom)
    {
        if (edit.OldString.Length == 0)
            return EditReplacementResolution.Error(EditFileErrorKind.InvalidEmptyOldString, "Cannot edit a non-empty file with an empty OldString.");

        if (normalizedNewString.Length == 0 && !EndsWithAnyNewline(edit.OldString))
        {
            var newlineOldString = TryAppendExistingNewline(buffer, edit.OldString);
            if (newlineOldString != null)
            {
                var deleteLine = ResolveCandidateSet(
                    buffer,
                    newlineOldString,
                    normalizedNewString,
                    edit.ReplaceAll,
                    editIndex,
                    EditMatchStrategy.Exact,
                    recovered: false,
                    priorRead,
                    []);
                if (deleteLine.Resolved != null || deleteLine.ErrorKind is EditFileErrorKind.AmbiguousMatch or EditFileErrorKind.OutsideReadRange)
                    return deleteLine;
            }
        }

        var bom = TryResolveBomHiddenFirstLineMatch(buffer, edit, normalizedNewString, editIndex, priorRead, fileHasBom);
        if (bom.HasResult)
            return bom.Resolution;

        var exact = ResolveCandidateSet(
            buffer,
            edit.OldString,
            normalizedNewString,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.Exact,
            recovered: false,
            priorRead,
            []);
        if (exact.Resolved != null || exact.ErrorKind is EditFileErrorKind.AmbiguousMatch or EditFileErrorKind.OutsideReadRange)
            return exact;

        if (edit.OldString.Length < MinRecoveryOldStringChars)
            return EditReplacementResolution.Error(EditFileErrorKind.NoMatch, $"String to replace was not found for edit {editIndex}.");

        var lineEnding = TryResolveLineEndingNormalizedMatch(buffer, edit, normalizedNewString, editIndex, priorRead);
        if (lineEnding.HasResult)
            return lineEnding.Resolution;

        if (TryDesanitize(edit.OldString, out var desanitized, out var desanitizeKind))
        {
            var desanitizedResult = ResolveCandidateSet(
                buffer,
                desanitized,
                normalizedNewString,
                edit.ReplaceAll,
                editIndex,
                EditMatchStrategy.Desanitized,
                recovered: true,
                priorRead,
                [new EditNormalizationNote(editIndex, desanitizeKind)]);
            if (desanitizedResult.Resolved != null || IsRecoverySpecificError(desanitizedResult.ErrorKind))
                return desanitizedResult;
        }

        var quote = TryResolveQuoteNormalizedMatch(buffer, edit, normalizedNewString, editIndex, priorRead);
        if (quote.HasResult)
            return quote.Resolution;

        var unescaped = UnescapeSearchString(edit.OldString, out var unescapedChanged);
        if (unescapedChanged)
        {
            var unescapedResult = ResolveCandidateSet(
                buffer,
                unescaped,
                normalizedNewString,
                edit.ReplaceAll,
                editIndex,
                EditMatchStrategy.EscapedStringNormalized,
                recovered: true,
                priorRead,
                [new EditNormalizationNote(editIndex, "escaped_string")]);
            if (unescapedResult.Resolved != null || IsRecoverySpecificError(unescapedResult.ErrorKind))
                return unescapedResult;
        }

        var trimmed = edit.OldString.Trim();
        if (trimmed.Length != edit.OldString.Length && trimmed.Length >= MinRecoveryOldStringChars)
        {
            var trimmedResult = ResolveCandidateSet(
                buffer,
                trimmed,
                normalizedNewString,
                edit.ReplaceAll,
                editIndex,
                EditMatchStrategy.TrimmedBoundary,
                recovered: true,
                priorRead,
                [new EditNormalizationNote(editIndex, "trimmed_boundary")]);
            if (trimmedResult.Resolved != null || IsRecoverySpecificError(trimmedResult.ErrorKind))
                return trimmedResult;
        }

        var indentation = TryResolveIndentationOnlyBlockMatch(buffer, edit, normalizedNewString, editIndex, priorRead);
        if (indentation.HasResult)
            return indentation.Resolution;

        var whitespace = TryResolveWhitespaceOnlyAnchoredBlockMatch(buffer, edit, normalizedNewString, editIndex, priorRead);
        if (whitespace.HasResult)
            return whitespace.Resolution;

        return EditReplacementResolution.Error(EditFileErrorKind.NoMatch, $"String to replace was not found for edit {editIndex}.");
    }

    private static EditReplacementResolution ResolveCandidateSet(
        string buffer,
        string actualOldString,
        string actualNewString,
        bool replaceAll,
        int editIndex,
        EditMatchStrategy strategy,
        bool recovered,
        ReadFileSnapshot? priorRead,
        IReadOnlyList<EditNormalizationNote> notes)
    {
        var candidates = FindAllOccurrences(buffer, actualOldString)
            .Select(start => new EditCandidate(start, actualOldString.Length))
            .ToArray();

        return ResolveCandidateSet(buffer, actualOldString, actualNewString, replaceAll, editIndex, strategy, recovered, priorRead, notes, candidates);
    }

    private static EditReplacementResolution ResolveCandidateSet(
        string buffer,
        string actualOldString,
        string actualNewString,
        bool replaceAll,
        int editIndex,
        EditMatchStrategy strategy,
        bool recovered,
        ReadFileSnapshot? priorRead,
        IReadOnlyList<EditNormalizationNote> notes,
        IReadOnlyList<EditCandidate> candidates)
    {
        if (candidates.Count > MaxRecoveryCandidates && recovered)
            return EditReplacementResolution.Error(EditFileErrorKind.RecoveryLimitExceeded, $"Recovered match for edit {editIndex} exceeded the recovery candidate limit. Provide a more exact OldString.", strategy);

        if (candidates.Count == 0)
            return EditReplacementResolution.Error(EditFileErrorKind.NoMatch, $"String to replace was not found for edit {editIndex}.");

        if (!replaceAll && candidates.Count > 1)
        {
            var kind = recovered ? EditFileErrorKind.RecoveryAmbiguous : EditFileErrorKind.AmbiguousMatch;
            return EditReplacementResolution.Error(kind, $"Found {candidates.Count} matches for edit {editIndex}, but replaceAll is false.", recovered ? strategy : null);
        }

        if (HasOverlappingCandidates(candidates))
            return EditReplacementResolution.Error(EditFileErrorKind.RecoveryAmbiguous, $"Recovered match for edit {editIndex} is ambiguous. Provide a more exact OldString.", strategy);

        var outsideRange = candidates
            .Where(candidate => !IsEditInsideReadRange(buffer, candidate, priorRead))
            .ToArray();

        if (outsideRange.Length > 0 && priorRead != null)
        {
            var kind = recovered ? EditFileErrorKind.RecoveryOutsideReadRange : EditFileErrorKind.OutsideReadRange;
            return EditReplacementResolution.Error(kind, $"Edit {editIndex} is outside the previously read range. Read the relevant lines before editing.", recovered ? strategy : null);
        }

        if (recovered && replaceAll && HasMixedRecoveredCandidateShape(buffer, candidates))
        {
            return EditReplacementResolution.Error(
                EditFileErrorKind.RecoverySemanticDifference,
                $"Recovered match for edit {editIndex} changes non-whitespace tokens. Provide the exact OldString.",
                strategy);
        }

        var selected = replaceAll ? candidates : [candidates[0]];
        var resolved = new ResolvedEditReplacement(
            editIndex,
            RequestedOldString: actualOldString,
            ActualOldString: actualOldString,
            ActualNewString: actualNewString,
            ReplaceAll: replaceAll,
            MatchStrategy: strategy,
            Recovered: recovered,
            NormalizationNotes: notes)
        {
            ReplacementCount = selected.Count
        };

        return new EditReplacementResolution(resolved, selected, null, null, null);
    }

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveLineEndingNormalizedMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        ReadFileSnapshot? priorRead)
    {
        var normalizedBuffer = NormalizeForLineEndingMatch(buffer, out var map);
        var normalizedOldString = NormalizeForLineEndingMatch(edit.OldString, out _);
        if (string.Equals(normalizedBuffer, buffer, StringComparison.Ordinal) &&
            string.Equals(normalizedOldString, edit.OldString, StringComparison.Ordinal))
        {
            return (false, default!);
        }

        var starts = FindAllOccurrences(normalizedBuffer, normalizedOldString).ToArray();
        var candidates = starts
            .Select(start => new EditCandidate(map[start], map[start + normalizedOldString.Length] - map[start]))
            .Distinct()
            .ToArray();

        var actualOld = candidates.Length > 0 ? buffer.Substring(candidates[0].Start, candidates[0].Length) : edit.OldString;
        var resolution = ResolveCandidateSet(
            buffer,
            actualOld,
            normalizedNewString,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.LineEndingNormalized,
            recovered: true,
            priorRead,
            [new EditNormalizationNote(editIndex, "line_endings")],
            candidates);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveBomHiddenFirstLineMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        ReadFileSnapshot? priorRead,
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
        var resolution = ResolveCandidateSet(
            buffer,
            edit.OldString,
            normalizedNewString,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.BomHiddenFirstLine,
            recovered: true,
            priorRead,
            [new EditNormalizationNote(editIndex, "bom_hidden_first_line")],
            [candidate]);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveQuoteNormalizedMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        ReadFileSnapshot? priorRead)
    {
        var normalizedBuffer = NormalizeQuotes(buffer);
        var normalizedOldString = NormalizeQuotes(edit.OldString);
        if (normalizedBuffer == buffer && normalizedOldString == edit.OldString)
            return (false, default!);

        var starts = FindAllOccurrences(normalizedBuffer, normalizedOldString).ToArray();
        var candidates = starts.Select(start => new EditCandidate(start, normalizedOldString.Length)).ToArray();
        var actualNewString = PreserveQuoteStyle(buffer, candidates.FirstOrDefault(), normalizedNewString, out var preserved);
        var notes = preserved
            ? [new EditNormalizationNote(editIndex, "curly_quotes")]
            : new[] { new EditNormalizationNote(editIndex, "quote_normalized") };

        var actualOld = candidates.Length > 0 ? buffer.Substring(candidates[0].Start, candidates[0].Length) : edit.OldString;
        var resolution = ResolveCandidateSet(
            buffer,
            actualOld,
            actualNewString,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.QuoteNormalized,
            recovered: true,
            priorRead,
            notes,
            candidates);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveIndentationOnlyBlockMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        ReadFileSnapshot? priorRead)
    {
        if (!edit.OldString.Contains('\n', StringComparison.Ordinal))
            return (false, default!);

        var requested = RemoveCommonIndentation(edit.OldString);
        var requestedNormalized = TrimTrailingNewlines(requested.NormalizedText);

        var lineCount = SplitPreservingLineEnds(edit.OldString).Length;
        var candidates = EnumerateLineBlockCandidates(buffer, lineCount)
            .Where(candidate => TrimTrailingNewlines(RemoveCommonIndentation(buffer.Substring(candidate.Start, candidate.Length)).NormalizedText) == requestedNormalized)
            .ToArray();

        var actualOld = candidates.Length > 0 ? buffer.Substring(candidates[0].Start, candidates[0].Length) : edit.OldString;
        var resolution = ResolveCandidateSet(
            buffer,
            actualOld,
            normalizedNewString,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.IndentationOnlyBlock,
            recovered: true,
            priorRead,
            [new EditNormalizationNote(editIndex, "indentation")],
            candidates);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static (bool HasResult, EditReplacementResolution Resolution) TryResolveWhitespaceOnlyAnchoredBlockMatch(
        string buffer,
        FileEditReplacement edit,
        string normalizedNewString,
        int editIndex,
        ReadFileSnapshot? priorRead)
    {
        var requestedLines = SplitLogicalLines(edit.OldString);
        var requestedNonEmpty = requestedLines.Where(line => line.Trim().Length > 0).ToArray();
        if (requestedNonEmpty.Length < 3)
            return (false, default!);

        var lineCount = requestedLines.Length;
        var requestedSignature = RemoveAllWhitespace(edit.OldString);
        var candidates = EnumerateLineBlockCandidates(buffer, lineCount)
            .Where(candidate =>
            {
                var actual = buffer.Substring(candidate.Start, candidate.Length);
                var actualLines = SplitLogicalLines(actual);
                var actualNonEmpty = actualLines.Where(line => line.Trim().Length > 0).ToArray();
                return actualNonEmpty.Length == requestedNonEmpty.Length &&
                       string.Equals(actualNonEmpty[0].Trim(), requestedNonEmpty[0].Trim(), StringComparison.Ordinal) &&
                       string.Equals(actualNonEmpty[^1].Trim(), requestedNonEmpty[^1].Trim(), StringComparison.Ordinal) &&
                       string.Equals(RemoveAllWhitespace(actual), requestedSignature, StringComparison.Ordinal);
            })
            .ToArray();

        var actualOld = candidates.Length > 0 ? buffer.Substring(candidates[0].Start, candidates[0].Length) : edit.OldString;
        var resolution = ResolveCandidateSet(
            buffer,
            actualOld,
            normalizedNewString,
            edit.ReplaceAll,
            editIndex,
            EditMatchStrategy.WhitespaceOnlyAnchoredBlock,
            recovered: true,
            priorRead,
            [new EditNormalizationNote(editIndex, "whitespace_anchored")],
            candidates);
        return (resolution.Resolved != null || IsRecoverySpecificError(resolution.ErrorKind), resolution);
    }

    private static string NormalizeEditNewString(
        string path,
        string newString,
        bool normalizeTrailingWhitespace,
        out IReadOnlyList<EditNormalizationNote> notes,
        int editIndex)
    {
        var normalized = newString;
        var localNotes = new List<EditNormalizationNote>();
        if (normalizeTrailingWhitespace && !IsMarkdownPath(path))
        {
            var stripped = StripTrailingWhitespace(normalized);
            if (stripped != normalized)
            {
                normalized = stripped;
                localNotes.Add(new EditNormalizationNote(editIndex, "trailing_whitespace"));
            }
        }

        notes = localNotes;
        return normalized;
    }

    private static bool ContainsNewOmissionPlaceholder(string oldString, string newString)
        => StandaloneEditOmissionPlaceholderRegex().IsMatch(newString) &&
           !StandaloneEditOmissionPlaceholderRegex().IsMatch(oldString);

    private static string StripTrailingWhitespace(string value)
        => TrailingWhitespaceRegex().Replace(value, string.Empty);

    private static bool IsMarkdownPath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".mdx", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<int> FindAllOccurrences(string value, string search)
    {
        if (search.Length == 0)
            return [];

        var starts = new List<int>();
        var index = 0;
        while (index <= value.Length)
        {
            var found = value.IndexOf(search, index, StringComparison.Ordinal);
            if (found < 0)
                break;
            starts.Add(found);
            index = found + Math.Max(1, search.Length);
        }

        return starts;
    }

    private static bool IsEditInsideReadRange(string buffer, EditCandidate candidate, ReadFileSnapshot? priorRead)
    {
        if (priorRead == null ||
            priorRead.Coverage is ReadFileCoverage.FullFile or ReadFileCoverage.EmptyFile)
        {
            return true;
        }

        if (priorRead.Coverage == ReadFileCoverage.Truncated)
            return false;

        var range = GetRange(buffer, candidate.Start, candidate.Length);
        var lastTouchedLine = range.EndColumn == 1 && range.EndLine > range.StartLine
            ? range.EndLine - 1
            : range.EndLine;

        return range.StartLine >= priorRead.StartLine &&
               lastTouchedLine <= priorRead.EndLine;
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
            EditFileErrorKind.RecoverySemanticDifference;

    private static string? TryAppendExistingNewline(string buffer, string oldString)
    {
        if (buffer.Contains(oldString + "\r\n", StringComparison.Ordinal))
            return oldString + "\r\n";

        if (buffer.Contains(oldString + "\n", StringComparison.Ordinal))
            return oldString + "\n";

        return null;
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

    private static bool TryDesanitize(string value, out string desanitized, out string kind)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<fnr>"] = "<function_results>",
            ["<n>"] = "<name>",
            ["</n>"] = "</name>",
            ["<o>"] = "<output>",
            ["</o>"] = "</output>",
            ["<e>"] = "<error>",
            ["</e>"] = "</error>",
            ["<s>"] = "<system>",
            ["</s>"] = "</system>",
            ["<r>"] = "<result>",
            ["</r>"] = "</result>",
            ["< META_START >"] = "<META_START>",
            ["< META_END >"] = "<META_END>",
            ["< EOT >"] = "<EOT>",
            ["< META >"] = "<META>",
            ["< SOS >"] = "<SOS>"
        };

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

    private static string PreserveQuoteStyle(string buffer, EditCandidate candidate, string newString, out bool preserved)
    {
        preserved = false;
        if (candidate.Length == 0 || candidate.Start < 0 || candidate.Start + candidate.Length > buffer.Length)
            return newString;

        var actual = buffer.Substring(candidate.Start, candidate.Length);
        var usesDouble = actual.Contains('“') || actual.Contains('”');
        var usesSingle = actual.Contains('‘') || actual.Contains('’');
        if (!usesDouble && !usesSingle)
            return newString;

        preserved = true;
        var builder = new StringBuilder(newString.Length);
        for (var i = 0; i < newString.Length; i++)
        {
            var ch = newString[i];
            if (usesDouble && ch == '"')
            {
                builder.Append(IsOpeningQuote(newString, i) ? '“' : '”');
                continue;
            }

            if (usesSingle && ch == '\'')
            {
                builder.Append(IsApostropheBetweenLetters(newString, i) ? '’' : IsOpeningQuote(newString, i) ? '‘' : '’');
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool IsOpeningQuote(string value, int index)
        => index == 0 ||
           char.IsWhiteSpace(value[index - 1]) ||
           "([{<".Contains(value[index - 1], StringComparison.Ordinal);

    private static bool IsApostropheBetweenLetters(string value, int index)
        => index > 0 &&
           index + 1 < value.Length &&
           char.IsLetter(value[index - 1]) &&
           char.IsLetter(value[index + 1]);

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
            if (text[i] == '\n' && i + 1 < text.Length)
                starts.Add(i + 1);
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
            if (text[i] != '\n')
                continue;

            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);

        return lines.ToArray();
    }

    private static string[] SplitLogicalLines(string text)
        => SplitPreservingLineEnds(text).Select(RemoveLineEnding).ToArray();

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

    private static string RemoveAllWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
                builder.Append(ch);
        }

        return builder.ToString();
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
                .Select(note => new FileEditNormalizationNote(note.EditIndex, note.Kind))
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

    [GeneratedRegex(@"[ \t]+(?=\r?$)", RegexOptions.Multiline)]
    private static partial Regex TrailingWhitespaceRegex();
}

public sealed record FileEditReplacement
{
    public required string OldString { get; init; }
    public required string NewString { get; init; }
    public bool ReplaceAll { get; init; }
}

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

internal sealed record EditNormalizationNote(int EditIndex, string Kind);

internal readonly record struct EditCandidate(int Start, int Length);

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
    IndentationOnlyBlock,
    WhitespaceOnlyAnchoredBlock
}

internal enum EditFileErrorKind
{
    InvalidArguments,
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
    RecoveryOutsideReadRange,
    RecoverySemanticDifference
}

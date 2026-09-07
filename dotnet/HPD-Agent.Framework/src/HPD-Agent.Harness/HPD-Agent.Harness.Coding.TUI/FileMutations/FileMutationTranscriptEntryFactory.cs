using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPDOS.ToolHarnesses.Middleware;
using MiddlewareFileMutationHunk = HPDOS.ToolHarnesses.Middleware.FileMutationHunk;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

internal static class FileMutationTranscriptEntryFactory
{
    private const int MaxCellDiagnostics = 64;

    public static string EntryKey(FileMutationAppliedEvent mutation)
    {
        var path = FileMutationTuiState.NormalizePath(mutation.Path);
        var id = string.IsNullOrWhiteSpace(mutation.ToolCallId)
            ? mutation.EventId ?? mutation.TraceId ?? Guid.NewGuid().ToString("N")
            : mutation.ToolCallId;
        return $"hpd.coding.file-mutation:{path}:{id}";
    }

    public static string EntryId(FileMutationAppliedEvent mutation)
        => $"file-mutation-{Math.Abs(EntryKey(mutation).GetHashCode(StringComparison.Ordinal))}";

    public static TranscriptEntry Create(FileMutationTranscriptModel model, AgentEvent evt)
        => new(
            Id: EntryId(model.Mutation),
            EntryKey: model.EntryKey,
            Cell: CreateCell(model),
            Metadata: TranscriptEntryMetadata.FromEvent(evt));

    public static void Apply(AgentTuiEventContext context, FileMutationTranscriptModel model, AgentEvent evt)
    {
        var entry = Create(model, evt).AsFinal();
        context.Shell.Transcript.FinalizeLive(model.EntryKey, entry, CommittedHistoryMutationPolicy.Reject);
    }

    public static TranscriptEntry CreateStandaloneDiagnostics(LanguageServerDiagnosticsReceivedEvent diagnostics)
    {
        var key = $"hpd.coding.diagnostics:{FileMutationTuiState.NormalizePath(diagnostics.Path)}";
        return new TranscriptEntry(
            Id: $"diagnostics-{Math.Abs(key.GetHashCode(StringComparison.Ordinal))}",
            EntryKey: key,
            Cell: CreateCell(diagnostics),
            Metadata: TranscriptEntryMetadata.FromEvent(diagnostics));
    }

    public static void ApplyStandaloneDiagnostics(
        AgentTuiEventContext context,
        LanguageServerDiagnosticsReceivedEvent diagnostics)
    {
        var entry = CreateStandaloneDiagnostics(diagnostics).AsFinal();
        context.Shell.Transcript.FinalizeLive(entry.EntryKey!, entry, CommittedHistoryMutationPolicy.Reject);
    }

    public static FileMutationCell CreateCell(FileMutationTranscriptModel model)
        => new(
            model.EntryKey,
            model.Mutation.Path,
            model.Mutation.DisplayPath,
            FileMutationDisplayFormatter.LabelFor(model.Mutation),
            MapKind(model.Mutation),
            new FileMutationDiffStat(
                model.Mutation.DiffStat.AddedLines,
                model.Mutation.DiffStat.RemovedLines),
            model.Mutation.Hunks.Select(CreateHunk).ToArray(),
            model.Mutation.HunksTruncated,
            model.Diagnostics is null ? [] : CreateDiagnostics(model.Diagnostics),
            model.Diagnostics is not null &&
            (model.Diagnostics.DiagnosticsTruncated || model.Diagnostics.Diagnostics.Count > MaxCellDiagnostics));

    public static CodingDiagnosticsCell CreateCell(LanguageServerDiagnosticsReceivedEvent diagnostics)
        => new(
            diagnostics.Path,
            diagnostics.Path,
            diagnostics.ErrorCount,
            diagnostics.WarningCount,
            CreateDiagnostics(diagnostics),
            diagnostics.DiagnosticsTruncated || diagnostics.Diagnostics.Count > MaxCellDiagnostics);

    private static FileMutationKind MapKind(FileMutationAppliedEvent mutation)
        => mutation.MutationKind switch
        {
            CodingFileMutationKind.Created => FileMutationKind.Created,
            CodingFileMutationKind.Deleted => FileMutationKind.Deleted,
            CodingFileMutationKind.Changed => mutation.Created ? FileMutationKind.Created : FileMutationKind.Modified,
            _ => FileMutationKind.Unknown
        };

    private static FileMutationHunk CreateHunk(MiddlewareFileMutationHunk hunk)
        => new(
            hunk.OldStart,
            hunk.OldLines,
            hunk.NewStart,
            hunk.NewLines,
            hunk.Lines.Select(CreateDiffLine).ToArray());

    private static FileMutationDiffLine CreateDiffLine(string rawLine)
    {
        if (string.IsNullOrEmpty(rawLine))
        {
            return new FileMutationDiffLine(FileMutationDiffLineKind.Context, "");
        }

        return rawLine[0] switch
        {
            '+' => new FileMutationDiffLine(FileMutationDiffLineKind.Added, rawLine[1..]),
            '-' => new FileMutationDiffLine(FileMutationDiffLineKind.Removed, rawLine[1..]),
            ' ' => new FileMutationDiffLine(FileMutationDiffLineKind.Context, rawLine[1..]),
            _ => new FileMutationDiffLine(FileMutationDiffLineKind.Context, rawLine)
        };
    }

    private static IReadOnlyList<CodingDiagnosticLine> CreateDiagnostics(LanguageServerDiagnosticsReceivedEvent diagnostics)
        => diagnostics.Diagnostics
            .Take(MaxCellDiagnostics)
            .Select(static diagnostic => new CodingDiagnosticLine(
                MapSeverity(diagnostic.Severity),
                diagnostic.Source.ToString(),
                diagnostic.Code,
                diagnostic.Line,
                diagnostic.Character,
                diagnostic.Message))
            .ToArray();

    private static CodingDiagnosticSeverity MapSeverity(LanguageServerDiagnosticSeverity severity)
        => severity switch
        {
            LanguageServerDiagnosticSeverity.Error => CodingDiagnosticSeverity.Error,
            LanguageServerDiagnosticSeverity.Warning => CodingDiagnosticSeverity.Warning,
            LanguageServerDiagnosticSeverity.Information => CodingDiagnosticSeverity.Information,
            LanguageServerDiagnosticSeverity.Hint => CodingDiagnosticSeverity.Hint,
            _ => CodingDiagnosticSeverity.Information
        };
}

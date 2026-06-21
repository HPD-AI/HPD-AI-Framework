using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

public sealed record FileMutationCell(
    string EntryKey,
    string Path,
    string DisplayPath,
    string Label,
    FileMutationKind Kind,
    FileMutationDiffStat DiffStat,
    IReadOnlyList<FileMutationHunk> Hunks,
    bool HunksTruncated,
    IReadOnlyList<CodingDiagnosticLine> Diagnostics,
    bool DiagnosticsTruncated) : TranscriptCell;

public enum FileMutationKind
{
    Created,
    Modified,
    Deleted,
    Renamed,
    Unknown
}

public sealed record FileMutationDiffStat(
    int AddedLines,
    int RemovedLines);

public sealed record FileMutationHunk(
    int OldStart,
    int OldLines,
    int NewStart,
    int NewLines,
    IReadOnlyList<FileMutationDiffLine> Lines);

public sealed record FileMutationDiffLine(
    FileMutationDiffLineKind Kind,
    string Text);

public enum FileMutationDiffLineKind
{
    Context,
    Added,
    Removed
}

public sealed record CodingDiagnosticsCell(
    string Path,
    string DisplayPath,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<CodingDiagnosticLine> Diagnostics,
    bool Truncated) : TranscriptCell;

public sealed record CodingDiagnosticLine(
    CodingDiagnosticSeverity Severity,
    string Source,
    string? Code,
    int Line,
    int Character,
    string Message);

public enum CodingDiagnosticSeverity
{
    Error,
    Warning,
    Information,
    Hint
}

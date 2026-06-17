using HPD.Agent;
using HPD.Events;

namespace HPDOS.ToolHarnesses.Middleware;

public abstract record FileMutationAppliedEvent : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    public override bool ShouldPersistToThread() => true;
    public required string ToolCallId { get; init; }
    public required string FunctionName { get; init; }
    public required string Path { get; init; }
    public required string DisplayPath { get; init; }
    public required CodingFileMutationKind MutationKind { get; init; }
    public required bool Created { get; init; }
    public required bool Changed { get; init; }
    public required FileMutationSnapshot Before { get; init; }
    public required FileMutationSnapshot After { get; init; }
    public required IReadOnlyList<FileMutationTextEdit> TextEdits { get; init; }
    public required IReadOnlyList<FileMutationHunk> Hunks { get; init; }
    public required bool HunksTruncated { get; init; }
    public required FileMutationDiffStat DiffStat { get; init; }
    public IReadOnlyList<FileMutationNote> Notes { get; init; } = [];
}

public sealed record FileEditAppliedEvent : FileMutationAppliedEvent
{
    public required int EditCount { get; init; }
    public required int ReplacementCount { get; init; }
    public required IReadOnlyList<FileEditAppliedReplacement> Replacements { get; init; }
    public required IReadOnlyList<FileEditNormalizationNote> Normalizations { get; init; }
}

public sealed record FileWriteAppliedEvent : FileMutationAppliedEvent
{
    public required FileWriteMode Mode { get; init; }
}

public enum FileWriteMode
{
    Create,
    FillEmpty,
    Rewrite
}

public sealed record FileMutationSnapshot(
    string? Text,
    string ContentHash,
    long ByteLength,
    int LineCount,
    string EncodingName,
    bool HasBom,
    string LineEnding,
    DateTimeOffset? LastWriteTimeUtc,
    bool TextOmitted,
    string? OmissionReason);

public sealed record FileMutationTextEdit(
    int EditIndex,
    FileMutationRange BeforeRange,
    FileMutationRange AfterRange,
    string? OldText,
    string? NewText,
    bool TextOmitted,
    string? OmissionReason);

public sealed record FileMutationRange(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    int StartOffset,
    int Length);

public sealed record FileMutationHunk(
    int OldStart,
    int OldLines,
    int NewStart,
    int NewLines,
    IReadOnlyList<string> Lines);

public sealed record FileMutationDiffStat(
    int AddedLines,
    int RemovedLines,
    int AddedChars,
    int RemovedChars);

public sealed record FileMutationNote(
    int? EditIndex,
    string Kind);

public sealed record FileEditAppliedReplacement(
    int EditIndex,
    bool ReplaceAll,
    int ReplacementCount,
    string MatchStrategy,
    bool Recovered,
    IReadOnlyList<FileMutationRange> BeforeRanges,
    IReadOnlyList<FileMutationRange> AfterRanges);

public sealed record FileEditNormalizationNote(
    int EditIndex,
    string Kind);

public interface IFileMutationLockProvider
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        string fullPath,
        CancellationToken cancellationToken);
}

public interface IFileMutationTextSink
{
    ValueTask<FileMutationSinkResult?> TryMutateTextAsync(
        FileMutationSinkRequest request,
        CancellationToken cancellationToken);
}

public sealed record FileMutationSinkRequest
{
    public required string ToolName { get; init; }
    public required string Path { get; init; }
    public string? BeforeText { get; init; }
    public required string AfterText { get; init; }
    public required CodingFileMutationKind Kind { get; init; }
    public IReadOnlyList<FileMutationTextEdit> TextEdits { get; init; } = [];
    public string? EncodingName { get; init; }
    public string? LineEnding { get; init; }
}

public sealed record FileMutationSinkResult
{
    public required string FinalText { get; init; }
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
    public long? ByteLength { get; init; }
    public string? ContentHash { get; init; }
    public bool WroteToDisk { get; init; }
}

public interface IFileMutationHistorySink
{
    ValueTask CaptureBeforeMutationAsync(
        FileMutationHistoryRequest request,
        CancellationToken cancellationToken);
}

public sealed record FileMutationHistoryRequest
{
    public required string ToolName { get; init; }
    public required string Path { get; init; }
    public required string BeforeText { get; init; }
    public required string ContentHash { get; init; }
    public required long ByteLength { get; init; }
}

internal sealed class NoOpFileMutationLockProvider : IFileMutationLockProvider
{
    public static readonly NoOpFileMutationLockProvider Instance = new();

    private NoOpFileMutationLockProvider()
    {
    }

    public ValueTask<IAsyncDisposable> AcquireAsync(
        string fullPath,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<IAsyncDisposable>(NoOpAsyncDisposable.Instance);

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static readonly NoOpAsyncDisposable Instance = new();

        private NoOpAsyncDisposable()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

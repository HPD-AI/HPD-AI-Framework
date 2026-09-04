using HPD.Agent;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal static class CodingExplorationTranscriptEntryFactory
{
    private const int MaxCellOperations = 64;

    public static string EntryKey(CodingExplorationGroup group) => $"coding.exploration:{group.GroupId}";

    public static string EntryId(CodingExplorationGroup group)
        => $"coding-exploration-{Math.Abs(EntryKey(group).GetHashCode(StringComparison.Ordinal))}";

    public static TranscriptEntry Create(CodingExplorationGroup group, AgentEvent evt)
        => new(
            Id: EntryId(group),
            EntryKey: EntryKey(group),
            Cell: CreateCell(group),
            Metadata: TranscriptEntryMetadata.FromEvent(evt));

    public static void Apply(AgentTuiEventContext context, CodingExplorationGroup group, AgentEvent evt)
    {
        var entry = Create(group, evt);
        if (group.CaptureIsActive())
        {
            context.Shell.Transcript.UpsertLive(entry.AsLive(), CommittedHistoryMutationPolicy.Reject);
        }
        else
        {
            context.Shell.Transcript.FinalizeLive(EntryKey(group), entry.AsFinal(), CommittedHistoryMutationPolicy.Reject);
        }
    }

    public static CodingExplorationCell CreateCell(CodingExplorationGroup group)
    {
        var capturedOperations = group.CaptureOperations();
        var operations = capturedOperations.Take(MaxCellOperations).ToArray();
        var omittedOperationCount = Math.Max(0, capturedOperations.Count - operations.Length);
        return new CodingExplorationCell(
            group.GroupId,
            group.MessageId,
            group.CaptureIsActive(),
            group.StartedAt,
            group.LastUpdatedAt,
            operations.Select(CreateOperation).ToArray(),
            CodingExplorationDisplayFormatter.BuildRows(operations, omittedOperationCount));
    }

    private static CodingExplorationOperationCell CreateOperation(CodingExplorationOperation operation)
        => new(
            operation.CallId,
            operation.ToolName,
            MapState(operation.Status),
            operation.ArgsJson,
            operation.StartedAt,
            operation.CompletedAt,
            CreateSummary(operation.Summary));

    private static CodingExplorationOperationState MapState(CodingExplorationOperationStatus status)
        => status switch
        {
            CodingExplorationOperationStatus.Pending => CodingExplorationOperationState.Pending,
            CodingExplorationOperationStatus.Running => CodingExplorationOperationState.Running,
            CodingExplorationOperationStatus.Completed => CodingExplorationOperationState.Completed,
            CodingExplorationOperationStatus.Failed => CodingExplorationOperationState.Failed,
            _ => CodingExplorationOperationState.Pending
        };

    private static CodingExplorationSummaryCell? CreateSummary(CodingExplorationSummary? summary)
    {
        if (summary is null)
        {
            return null;
        }

        CodingExplorationSummaryCell cell = summary switch
        {
            ReadFileExplorationSummary read => new ReadFileExplorationSummaryCell
            {
                StartLine = read.StartLine,
                LinesRead = read.LinesRead,
                TotalLines = read.TotalLines,
                Coverage = read.Coverage,
                Unchanged = read.Unchanged
            },
            GrepExplorationSummary grep => new GrepExplorationSummaryCell
            {
                Pattern = grep.Pattern,
                OutputMode = grep.OutputMode,
                TotalResults = grep.TotalResults,
                TotalMatches = grep.TotalMatches,
                Status = grep.Status
            },
            GlobExplorationSummary glob => new GlobExplorationSummaryCell
            {
                Pattern = glob.Pattern,
                OriginalPattern = glob.OriginalPattern,
                TotalMatches = glob.TotalMatches,
                MatchesRead = glob.MatchesRead,
                IgnoredCount = glob.IgnoredCount
            },
            ListDirectoryExplorationSummary list => new ListDirectoryExplorationSummaryCell
            {
                Recursive = list.Recursive,
                EntriesRead = list.EntriesRead,
                TotalEntries = list.TotalEntries,
                IgnoredCount = list.IgnoredCount
            },
            _ => new UnknownExplorationSummaryCell()
        };

        return cell with
        {
            Path = summary.Path,
            Truncated = summary.Truncated,
            TruncationReason = summary.TruncationReason,
            HasMore = summary.HasMore,
            IsError = summary.IsError,
            ErrorMessage = summary.ErrorMessage
        };
    }
}

using System.Globalization;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal static class CodingCommandTranscriptEntryFactory
{
    private const int SnapshotHeadRows = 6;
    private const int SnapshotTailRows = 26;
    private const int MaxSnapshotRows = SnapshotHeadRows + SnapshotTailRows;

    public static string EntryKey(string commandId) => $"coding.command:{commandId}";

    public static string EntryId(string commandId) => $"coding-command-{commandId}";

    public static TranscriptEntry Create(CodingCommandCell cell, AgentEvent evt)
        => new(
            Id: EntryId(cell.CommandId),
            EntryKey: EntryKey(cell.CommandId),
            Cell: cell,
            Metadata: TranscriptEntryMetadata.FromEvent(evt));

    public static CodingCommandCell CreateCell(CodingCommandExecutionState state)
    {
        var snapshot = state.Output.CreateSnapshot(
            headRows: SnapshotHeadRows,
            tailRows: SnapshotTailRows,
            maxVisibleRows: MaxSnapshotRows);
        var summary = CodingCommandRenderText.ShouldRenderSummary(state)
            ? CodingCommandRenderText.BuildSummary(state)
            : "";
        return new CodingCommandCell(
            state.CommandId,
            state.ToolCallId,
            state.FunctionName,
            state.Command,
            state.DisplayCommand,
            state.BaseCommand,
            state.Category,
            state.WorkingDirectory,
            state.Shell,
            MapState(state.DisplayState),
            state.StartedAt,
            state.CompletedAt,
            state.BackgroundedAt,
            state.ProcessId,
            state.TimeoutMilliseconds,
            state.ExitCode,
            state.CompletionKind?.ToString(),
            state.DurationMilliseconds is { } duration
                ? TimeSpan.FromMilliseconds(duration)
                : null,
            state.IsBackground,
            state.AutoBackgroundEligible,
            state.BackgroundTaskId,
            state.OutputObserved,
            state.OutputTruncated,
            state.OutputEventsSuppressed,
            state.DrainTimedOut,
            state.BinaryOutputObserved,
            state.StdoutBytes,
            state.StderrBytes,
            state.CombinedOutputBytes,
            state.CombinedBytesDiscarded,
            snapshot.Lines.Select(static line => new CodingCommandOutputLine(
                    MapStream(line.Stream),
                    line.Text))
                .ToArray(),
            new CodingCommandOutputWindow(
                snapshot.HeadLineCount,
                snapshot.OmittedLineCount,
                snapshot.Truncated,
                snapshot.Suppressed,
                snapshot.Binary),
            CreateArtifacts(state.Artifacts),
            summary);
    }

    public static string SnapshotKey(CodingCommandCell cell)
    {
        var lines = string.Join(
            "\n",
            cell.Output.Select(static line => $"{line.Stream}:{line.Text}"));
        var artifacts = string.Join(
            "\n",
            cell.Artifacts.Select(static artifact =>
                $"{artifact.Kind}:{artifact.Path}:{artifact.ContentId}:{artifact.ByteLength?.ToString(CultureInfo.InvariantCulture)}"));
        return string.Join(
            "\u001f",
            VerbFor(cell.State),
            cell.DisplayCommand,
            lines,
            cell.OutputWindow.OmittedLineCount.ToString(CultureInfo.InvariantCulture),
            cell.OutputWindow.HeadLineCount.ToString(CultureInfo.InvariantCulture),
            cell.OutputWindow.Truncated.ToString(),
            cell.OutputWindow.Suppressed.ToString(),
            cell.OutputWindow.Binary.ToString(),
            cell.Summary,
            artifacts);
    }

    public static string VerbFor(CodingCommandTranscriptState state)
        => state switch
        {
            CodingCommandTranscriptState.Running => "Running",
            CodingCommandTranscriptState.Backgrounded => "Backgrounded",
            CodingCommandTranscriptState.Completed => "Ran",
            CodingCommandTranscriptState.Failed => "Failed",
            CodingCommandTranscriptState.Cancelled => "Cancelled",
            CodingCommandTranscriptState.TimedOut => "Timed out",
            _ => "Exited"
        };

    private static CodingCommandTranscriptState MapState(CodingCommandDisplayState state)
        => state switch
        {
            CodingCommandDisplayState.Running => CodingCommandTranscriptState.Running,
            CodingCommandDisplayState.Backgrounded => CodingCommandTranscriptState.Backgrounded,
            CodingCommandDisplayState.Completed => CodingCommandTranscriptState.Completed,
            CodingCommandDisplayState.Failed => CodingCommandTranscriptState.Failed,
            CodingCommandDisplayState.Cancelled => CodingCommandTranscriptState.Cancelled,
            CodingCommandDisplayState.TimedOut => CodingCommandTranscriptState.TimedOut,
            _ => CodingCommandTranscriptState.Exited
        };

    private static CodingCommandOutputStream MapStream(ExecuteCommandStreamKind stream)
        => stream switch
        {
            ExecuteCommandStreamKind.Stderr => CodingCommandOutputStream.Stderr,
            _ => CodingCommandOutputStream.Stdout
        };

    private static IReadOnlyList<CodingCommandArtifactInfo> CreateArtifacts(CodingCommandArtifacts artifacts)
    {
        var result = new List<CodingCommandArtifactInfo>();
        AddArtifact(result, "stdout", artifacts.StdoutArtifactPath, artifacts.StdoutContentId);
        AddArtifact(result, "stderr", artifacts.StderrArtifactPath, artifacts.StderrContentId);
        AddArtifact(result, "combined", artifacts.CombinedOutputArtifactPath, artifacts.CombinedOutputContentId);
        AddArtifact(result, "stdout-local", artifacts.StdoutLocalPath, null);
        AddArtifact(result, "stderr-local", artifacts.StderrLocalPath, null);
        AddArtifact(result, "combined-local", artifacts.CombinedOutputLocalPath, null);
        return result;
    }

    private static void AddArtifact(
        List<CodingCommandArtifactInfo> artifacts,
        string kind,
        string? path,
        string? contentId)
    {
        if (path is null && contentId is null)
        {
            return;
        }

        artifacts.Add(new CodingCommandArtifactInfo(kind, path, contentId, ByteLength: null));
    }
}

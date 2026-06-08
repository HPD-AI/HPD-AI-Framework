using System.Globalization;
using HPD.Agent.TUI.Models;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Views;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal static class CodingCommandTranscriptEntryFactory
{
    public static string EntryKey(string commandId) => $"coding.command:{commandId}";

    public static string EntryId(string commandId) => $"coding-command-{commandId}";

    public static TranscriptEntry Create(CodingCommandExecutionState state, AgentEvent evt)
        => new(
            Id: EntryId(state.CommandId),
            EntryKey: EntryKey(state.CommandId),
            Cell: new CustomComponentCell(
                Label: $"• {CodingCommandRenderText.VerbFor(state)} {state.DisplayCommand}",
                Component: new CodingCommandExecutionView(state),
                Indent: 0),
            Metadata: TranscriptEntryMetadata.FromEvent(evt));

    public static string SnapshotKey(CodingCommandExecutionState state)
    {
        var snapshot = state.Output.CreateSnapshot();
        var lines = string.Join(
            "\n",
            snapshot.Lines.Select(static line => $"{line.Stream}:{line.Text}"));
        var summary = CodingCommandRenderText.ShouldRenderSummary(state)
            ? CodingCommandRenderText.BuildSummary(state)
            : "";
        return string.Join(
            "\u001f",
            CodingCommandRenderText.VerbFor(state),
            state.DisplayCommand,
            lines,
            snapshot.OmittedLineCount.ToString(CultureInfo.InvariantCulture),
            snapshot.HeadLineCount.ToString(CultureInfo.InvariantCulture),
            snapshot.Truncated.ToString(),
            snapshot.Suppressed.ToString(),
            snapshot.Binary.ToString(),
            summary);
    }
}

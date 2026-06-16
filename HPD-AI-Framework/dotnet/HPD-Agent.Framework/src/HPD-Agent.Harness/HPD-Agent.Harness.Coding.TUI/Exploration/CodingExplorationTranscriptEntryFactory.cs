using HPD.Agent;
using HPD.Agent.TUI.Models;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration.Views;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal static class CodingExplorationTranscriptEntryFactory
{
    public static string EntryKey(CodingExplorationGroup group) => $"coding.exploration:{group.GroupId}";

    public static string EntryId(CodingExplorationGroup group)
        => $"coding-exploration-{Math.Abs(EntryKey(group).GetHashCode(StringComparison.Ordinal))}";

    public static TranscriptEntry Create(CodingExplorationGroup group, AgentEvent evt)
        => new(
            Id: EntryId(group),
            EntryKey: EntryKey(group),
            Cell: new CustomComponentCell(
                Label: group.CaptureIsActive() ? "• Exploring" : "• Explored",
                Component: new CodingExplorationTranscriptView(group),
                Indent: 0),
            Metadata: TranscriptEntryMetadata.FromEvent(evt));
}

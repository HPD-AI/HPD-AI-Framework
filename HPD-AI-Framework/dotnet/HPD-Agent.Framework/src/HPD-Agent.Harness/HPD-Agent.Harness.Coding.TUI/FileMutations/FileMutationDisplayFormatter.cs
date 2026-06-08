using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

internal static class FileMutationDisplayFormatter
{
    public static string ActionFor(FileMutationAppliedEvent mutation)
        => mutation switch
        {
            FileWriteAppliedEvent { Mode: FileWriteMode.Create } => "Created",
            FileWriteAppliedEvent { Mode: FileWriteMode.FillEmpty } => "Filled",
            FileWriteAppliedEvent { Mode: FileWriteMode.Rewrite } => "Rewrote",
            FileEditAppliedEvent when mutation.Created => "Created",
            FileEditAppliedEvent when mutation.MutationKind == CodingFileMutationKind.Deleted => "Deleted",
            FileEditAppliedEvent => "Edited",
            _ => mutation.MutationKind switch
            {
                CodingFileMutationKind.Created => "Created",
                CodingFileMutationKind.Deleted => "Deleted",
                _ => "Edited"
            }
        };

    public static string StatsFor(FileMutationAppliedEvent mutation)
        => $"(+{mutation.DiffStat.AddedLines} -{mutation.DiffStat.RemovedLines})";

    public static string LabelFor(FileMutationAppliedEvent mutation)
        => $"• {ActionFor(mutation)} {mutation.DisplayPath} {StatsFor(mutation)}";
}

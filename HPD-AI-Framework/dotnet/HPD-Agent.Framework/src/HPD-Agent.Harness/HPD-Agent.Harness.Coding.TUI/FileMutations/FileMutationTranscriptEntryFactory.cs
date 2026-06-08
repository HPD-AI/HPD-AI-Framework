using HPD.Agent.TUI.Models;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Views;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

internal static class FileMutationTranscriptEntryFactory
{
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
            Cell: new CustomComponentCell(
                Label: FileMutationDisplayFormatter.LabelFor(model.Mutation),
                Component: new FileMutationTranscriptView(model),
                Indent: 0),
            Metadata: TranscriptEntryMetadata.FromEvent(evt));

    public static TranscriptEntry CreateStandaloneDiagnostics(LanguageServerDiagnosticsReceivedEvent diagnostics)
    {
        var key = $"hpd.coding.diagnostics:{FileMutationTuiState.NormalizePath(diagnostics.Path)}";
        return new TranscriptEntry(
            Id: $"diagnostics-{Math.Abs(key.GetHashCode(StringComparison.Ordinal))}",
            EntryKey: key,
            Cell: new CustomComponentCell(
                Label: $"• Diagnostics {diagnostics.Path}",
                Component: new DiagnosticsTranscriptView(diagnostics),
                Indent: 0),
            Metadata: TranscriptEntryMetadata.FromEvent(diagnostics));
    }
}

using HPD.Agent.TUI.Composition;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Handlers;

internal sealed class LanguageServerDiagnosticsTuiHandler : AgentTuiEventHandler<LanguageServerDiagnosticsReceivedEvent>
{
    public override ValueTask HandleAsync(
        LanguageServerDiagnosticsReceivedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var diagnosticsState = context.State.GetOrCreate(
            CodingDiagnosticsTuiState.StateKey,
            static () => new CodingDiagnosticsTuiState());
        diagnosticsState.Update(evt);

        var mutationState = context.State.GetOrCreate(
            FileMutationTuiState.StateKey,
            static () => new FileMutationTuiState());
        if (mutationState.TryGetLatestByPath(evt.Path, out var mutation))
        {
            mutation.SetDiagnostics(evt);
            context.Shell.Transcript.Update(FileMutationTranscriptEntryFactory.Create(mutation, evt));
        }
        else if (evt.ErrorCount > 0 || evt.WarningCount > 0 || evt.DiagnosticsTruncated)
        {
            context.Shell.Transcript.Update(FileMutationTranscriptEntryFactory.CreateStandaloneDiagnostics(evt));
        }

        return ValueTask.CompletedTask;
    }
}

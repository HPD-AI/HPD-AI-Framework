using HPD.Agent.TUI.Composition;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Handlers;

internal sealed class FileMutationTuiHandler : AgentTuiEventHandler<FileMutationAppliedEvent>
{
    public override ValueTask HandleAsync(
        FileMutationAppliedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var state = context.State.GetOrCreate(
            FileMutationTuiState.StateKey,
            static () => new FileMutationTuiState());
        var model = state.Add(evt);

        var diagnostics = context.State.GetOrCreate(
            CodingDiagnosticsTuiState.StateKey,
            static () => new CodingDiagnosticsTuiState());
        if (diagnostics.LatestByPath.TryGetValue(FileMutationTuiState.NormalizePath(evt.Path), out var latestDiagnostics))
        {
            model.SetDiagnostics(latestDiagnostics);
        }

        FileMutationTranscriptEntryFactory.Apply(context, model, evt);
        return ValueTask.CompletedTask;
    }
}

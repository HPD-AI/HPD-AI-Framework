using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Runtime;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.LanguageServers;

internal sealed class LanguageServerStatusTuiHandler : AgentTuiEventHandler<LanguageServerStatusSnapshotEvent>
{
    public override ValueTask HandleAsync(
        LanguageServerStatusSnapshotEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        // A persisted snapshot is historical evidence, not proof that its process survived a restart.
        if (context.DeliveryMode != AgentTuiEventDeliveryMode.Live)
            return ValueTask.CompletedTask;

        context.State.GetOrCreate(
                CodingLanguageServerTuiState.StateKey,
                static () => new CodingLanguageServerTuiState())
            .Replace(evt.Servers);
        return ValueTask.CompletedTask;
    }
}

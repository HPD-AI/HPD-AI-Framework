using HPDAgent.Graph.Connectors.Abstractions.Events;

namespace HPDAgent.Graph.Connectors.Core.Dedupe;

public interface IWorkflowSourceDedupeService
{
    Task<bool> ShouldDispatchAsync(
        WorkflowSourceEmittedEvent evt,
        CancellationToken ct = default);
}

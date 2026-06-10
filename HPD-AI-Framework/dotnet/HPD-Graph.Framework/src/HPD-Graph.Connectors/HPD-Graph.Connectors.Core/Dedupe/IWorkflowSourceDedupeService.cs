using HPD.Graph.Connectors.Abstractions.Events;

namespace HPD.Graph.Connectors.Core.Dedupe;

public interface IWorkflowSourceDedupeService
{
    Task<bool> ShouldDispatchAsync(
        WorkflowSourceEmittedEvent evt,
        CancellationToken ct = default);
}

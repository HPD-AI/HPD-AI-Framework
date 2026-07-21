using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Composition;

/// <summary>
/// Reconciles current TUI state from an authoritative durable-thread snapshot.
/// </summary>
/// <remarks>
/// The reconciler runs when hydration obtains the snapshot and again after historical
/// event batches. This lets event handlers rebuild historical presentation without
/// allowing replayed lifecycle events to determine current shell state.
/// </remarks>
public interface IAgentTuiThreadStateReconciler
{
    /// <summary>Applies authoritative thread state to the current TUI session.</summary>
    ValueTask ReconcileAsync(
        AgentTuiThreadState threadState,
        AgentTuiEventContext context,
        CancellationToken cancellationToken);
}

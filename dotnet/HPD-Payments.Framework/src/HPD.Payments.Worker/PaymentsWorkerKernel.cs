using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.DurableWork;

namespace HPD.Payments.Worker;

/// <summary>Transport/process coordinator for durable work; domain completion remains owner-postcondition evidence.</summary>
public static class PaymentsWorkerKernel
{
    /// <summary>Begins one fenced attempt without treating process activation as success.</summary>
    public static WorkProtocolTransition Begin(WorkProtocolState state, string workerId, NamedTime expiresAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.TryClaim(workerId, expiresAt);
    }

    /// <summary>Records the exact current-epoch observation returned by the owner-specific handler.</summary>
    public static WorkProtocolTransition Complete(WorkProtocolState claimed, OwnerGeneration epoch, WorkAttemptObservation observation)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        return claimed.Observe(epoch, observation);
    }

    /// <summary>Recovers a process-dead claim through expiry, preserving uncertainty until owner reconciliation.</summary>
    public static WorkProtocolTransition RecoverExpired(WorkProtocolState claimed, NamedTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        return claimed.ExpireClaim(observedAt);
    }
}

using HPD.Agent.Authority;

namespace HPD.Agent.Middleware;

/// <summary>Observes one bounded, owner-neutral Agent control envelope.</summary>
/// <remarks>
/// A hook is an ingress adapter, not durable authority. An observation becomes
/// authoritative only through the registered authority journal protocol.
/// </remarks>
public interface IAgentControlHook
{
    /// <summary>Observes a control envelope without transferring domain ownership.</summary>
    /// <param name="envelope">The validated immutable envelope.</param>
    /// <param name="waitCancellation">Cancels only the caller's wait.</param>
    /// <returns>A closed observation disposition.</returns>
    ValueTask<AgentControlObservationResult> ObserveAsync(
        AgentControlEnvelope envelope,
        CancellationToken waitCancellation = default);
}

/// <summary>Reports the closed result of observing one control envelope.</summary>
public abstract record AgentControlObservationResult
{
    private AgentControlObservationResult() { }

    /// <summary>Reports that at least one configured participant observed the envelope.</summary>
    public sealed record Observed : AgentControlObservationResult;

    /// <summary>Reports that no configured participant handled the envelope.</summary>
    public sealed record NotHandled : AgentControlObservationResult;

    /// <summary>Reports a fail-closed rejection using a bounded non-secret code.</summary>
    public sealed record Rejected : AgentControlObservationResult
    {
        /// <summary>Initializes a rejected result.</summary>
        /// <param name="safeCode">A bounded non-secret diagnostic code.</param>
        /// <exception cref="ArgumentException"><paramref name="safeCode"/> is invalid.</exception>
        public Rejected(BoundedAscii safeCode)
        {
            if (!safeCode.IsValid) throw new ArgumentException("A safe rejection code is required.", nameof(safeCode));
            SafeCode = safeCode;
        }

        /// <summary>Gets the bounded non-secret rejection code.</summary>
        public BoundedAscii SafeCode { get; }
    }
}

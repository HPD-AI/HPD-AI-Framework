using HPD.Payments.Contracts.HeldPosition.QuotaWallet;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Runtime.QuotaWallet;

/// <summary>Identifies the monotone state of one exact quota reservation.</summary>
public enum QuotaReservationState
{
    /// <summary>No valid state.</summary>
    None = 0,
    /// <summary>The reservation is active and capacity is unavailable elsewhere.</summary>
    Held = 1,
    /// <summary>Acknowledgement is uncertain and capacity remains unavailable.</summary>
    Indeterminate = 2,
    /// <summary>The named reservation was consumed.</summary>
    Consumed = 3,
    /// <summary>Proven non-occurrence permitted release before any possible effect.</summary>
    Released = 4,
    /// <summary>Unresolved consequence remains owner-addressable.</summary>
    Residual = 5,
}

/// <summary>Applies monotone transitions to one quota reservation identity.</summary>
public sealed record QuotaReservationProtocol
{
    /// <summary>Gets the semantic operation identity.</summary>
    public SemanticId OperationId { get; }
    /// <summary>Gets the retained positive quantity.</summary>
    public long Quantity { get; }
    /// <summary>Gets the current state.</summary>
    public QuotaReservationState State { get; }

    private QuotaReservationProtocol(SemanticId operationId, long quantity, QuotaReservationState state) =>
        (OperationId, Quantity, State) = (operationId, quantity, state);

    /// <summary>Creates a reservation only from an accepted or indeterminate quota admission.</summary>
    public static QuotaReservationProtocol FromAdmission(SemanticId operationId, QuotaAdmissionResult admission)
    {
        if (!operationId.IsValid || admission.Kind is not (QuotaAdmissionKind.Accepted or QuotaAdmissionKind.Indeterminate) ||
            admission.RetainedReservation <= 0) throw new ArgumentException("Quota admission cannot create a reservation.");
        return new(operationId, admission.RetainedReservation, admission.Kind == QuotaAdmissionKind.Accepted
            ? QuotaReservationState.Held : QuotaReservationState.Indeterminate);
    }

    /// <summary>Consumes the exact reservation after the owner fact is admitted.</summary>
    public QuotaReservationProtocol Consume() => State == QuotaReservationState.Held
        ? new(OperationId, Quantity, QuotaReservationState.Consumed)
        : throw new InvalidOperationException("Only a definite active reservation may be consumed.");

    /// <summary>Releases capacity only after proof of non-occurrence and no possible external effect.</summary>
    public QuotaReservationProtocol Release(bool nonOccurrenceProven, bool possibleExternalEffect) =>
        State == QuotaReservationState.Held && nonOccurrenceProven && !possibleExternalEffect
            ? new(OperationId, Quantity, QuotaReservationState.Released)
            : throw new InvalidOperationException("Quota release requires definite pre-effect non-occurrence.");

    /// <summary>Retains an unresolved reservation as explicit residue.</summary>
    public QuotaReservationProtocol RetainResidue() => State is QuotaReservationState.Held or QuotaReservationState.Indeterminate
        ? new(OperationId, Quantity, QuotaReservationState.Residual)
        : throw new InvalidOperationException("A terminal quota reservation cannot become residue.");
}

/// <summary>Validates a generation-pinned wallet plan at consumption time.</summary>
public static class WalletPlanAdmission
{
    /// <summary>Returns Rejected when the plan is stale or expired and Indeterminate when an effect may already have occurred.</summary>
    public static QuotaAdmissionKind Admit(IReadOnlyList<WalletSourceSlice> plan,
        IReadOnlyDictionary<SemanticId, OwnerGeneration> currentGenerations, bool expiryCrossed, bool possibleExternalEffect)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(currentGenerations);
        if (plan.Count == 0) return QuotaAdmissionKind.Rejected;
        if (possibleExternalEffect) return QuotaAdmissionKind.Indeterminate;
        if (expiryCrossed || plan.Any(slice => !currentGenerations.TryGetValue(slice.LotId, out OwnerGeneration generation) || generation != slice.Generation))
            return QuotaAdmissionKind.Rejected;
        return QuotaAdmissionKind.Accepted;
    }
}

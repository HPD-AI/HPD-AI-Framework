using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Runtime.Publication;

/// <summary>Immutable adapter-neutral state for one audience-specific publication obligation.</summary>
public sealed record PublicationProtocolState
{
    /// <summary>Gets the immutable obligation.</summary>
    public PublicationObligationFact Obligation { get; }
    /// <summary>Gets the number of retained delivery attempts.</summary>
    public uint Attempt { get; }
    /// <summary>Gets the current audience-specific disposition.</summary>
    public PublicationDisposition Disposition { get; }
    /// <summary>Gets the last delivery identity, when an attempt exists.</summary>
    public SemanticId DeliveryId { get; }
    /// <summary>Gets whether an unacknowledged send may have reached the audience.</summary>
    public bool AwaitingReconciliation { get; }

    private PublicationProtocolState(PublicationObligationFact obligation, uint attempt,
        PublicationDisposition disposition, SemanticId deliveryId, bool awaitingReconciliation)
    {
        Obligation = obligation;
        Attempt = attempt;
        Disposition = disposition;
        DeliveryId = deliveryId;
        AwaitingReconciliation = awaitingReconciliation;
    }

    /// <summary>Creates a required, discoverable publication state.</summary>
    public static PublicationProtocolState Create(PublicationObligationFact obligation)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        return new(obligation, 0, PublicationDisposition.Required, default, false);
    }

    /// <summary>Records crossing an audience send boundary.</summary>
    public PublicationProtocolTransition Dispatch(SemanticId deliveryId)
    {
        if (!deliveryId.IsValid || deliveryId.Scope != Obligation.PublicationId.Scope)
            return Reject("invalid-delivery");
        if (Disposition is PublicationDisposition.Acknowledged or PublicationDisposition.Exhausted)
            return Reject("terminal-publication");
        if (AwaitingReconciliation)
            return Reject("reconciliation-required");
        return Accept(new(Obligation, checked(Attempt + 1), PublicationDisposition.Attempted, deliveryId, true), "dispatch-attempted");
    }

    /// <summary>Admits an acknowledgement only for the exact current delivery identity.</summary>
    public PublicationProtocolTransition Acknowledge(SemanticId deliveryId)
    {
        if (!AwaitingReconciliation || deliveryId != DeliveryId)
            return Reject("acknowledgement-mismatch");
        return Accept(new(Obligation, Attempt, PublicationDisposition.Acknowledged, DeliveryId, false), "acknowledged");
    }

    /// <summary>Records reconciliation of an unacknowledged attempt.</summary>
    /// <param name="acknowledged">Whether admitted audience evidence proves acknowledgement.</param>
    /// <param name="attemptBudgetExhausted">Whether the externally declared attempt budget is exhausted.</param>
    public PublicationProtocolTransition Reconcile(bool acknowledged, bool attemptBudgetExhausted)
    {
        if (!AwaitingReconciliation)
            return Reject("reconciliation-not-required");
        var disposition = acknowledged
            ? PublicationDisposition.Acknowledged
            : attemptBudgetExhausted ? PublicationDisposition.Exhausted : PublicationDisposition.RedeliveryRequired;
        return Accept(new(Obligation, Attempt, disposition, DeliveryId, false),
            acknowledged ? "reconciled-acknowledged" : attemptBudgetExhausted ? "delivery-exhausted" : "redelivery-required");
    }

    /// <summary>Records audience or derivative residue without claiming global deletion or acknowledgement.</summary>
    public PublicationProtocolTransition RetainResidue()
    {
        if (Disposition == PublicationDisposition.Acknowledged)
            return Reject("already-acknowledged");
        return Accept(new(Obligation, Attempt, PublicationDisposition.Residual, DeliveryId, false), "residue-retained");
    }

    private PublicationProtocolTransition Reject(string code) => new(this, false, code);
    private static PublicationProtocolTransition Accept(PublicationProtocolState state, string code) => new(state, true, code);
}

/// <summary>Represents one immutable publication transition result.</summary>
public sealed record PublicationProtocolTransition
{
    /// <summary>Gets the resulting state.</summary>
    public PublicationProtocolState State { get; }
    /// <summary>Gets whether the transition was accepted.</summary>
    public bool Accepted { get; }
    /// <summary>Gets a bounded stable result code.</summary>
    public string Code { get; }

    internal PublicationProtocolTransition(PublicationProtocolState state, bool accepted, string code) =>
        (State, Accepted, Code) = (state, accepted, code);
}

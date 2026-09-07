namespace HPD.Events;

/// <summary>Reports that routed transport rejected a delivery which revisited one coordinator.</summary>
/// <param name="RejectedEventType">The runtime type name of the rejected domain event.</param>
/// <param name="CoordinatorId">The process-local coordinator identity that detected re-entry.</param>
public sealed record EventRouteReentryRejectedEvent(
    string RejectedEventType,
    string CoordinatorId) : Event
{
    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;

    /// <inheritdoc />
    public override EventKind Kind => EventKind.Diagnostic;
}

using System.Text.Json.Serialization;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Card;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides the closed AOT-safe codec for card lifecycle conservation projections.</summary>
public static class PaymentsCardLifecycleFactCodecs
{
    /// <summary>Gets the exact card-lifecycle state codec.</summary>
    public static PaymentsFactJsonCodec<CardLifecycleState> State { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.card-lifecycle-state.v1", PaymentsCardLifecycleJsonContext.Default.CardLifecyclePayload,
        CardLifecyclePayload.From, static payload => payload.ToValue());
}

internal sealed record CardLifecyclePayload(PaymentsIdentityPayload LifecycleId, string Unit, decimal Authorized,
    decimal Captured, decimal Voided, decimal Refunded, decimal Disputed, decimal ChargedBack, ulong Generation,
    PaymentsIdentityPayload LastOperationId)
{
    internal static CardLifecyclePayload From(CardLifecycleState value) => new(PaymentsIdentityPayload.From(value.LifecycleId),
        value.Unit, value.Authorized, value.Captured, value.Voided, value.Refunded, value.Disputed, value.ChargedBack,
        value.Generation.Value, PaymentsIdentityPayload.From(value.LastOperationId));
    internal CardLifecycleState ToValue() => CardLifecycleState.Restore(LifecycleId.ToValue(), Unit, Authorized, Captured,
        Voided, Refunded, Disputed, ChargedBack, OwnerGeneration.Create(Generation), LastOperationId.ToValue());
}

[JsonSerializable(typeof(CardLifecyclePayload))]
internal sealed partial class PaymentsCardLifecycleJsonContext : JsonSerializerContext;

using System.Text.Json.Serialization;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Settlement;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides the closed AOT-safe codec for settlement/accounting projections.</summary>
public static class PaymentsSettlementAccountingFactCodecs
{
    /// <summary>Gets the exact settlement/accounting projection codec.</summary>
    public static PaymentsFactJsonCodec<SettlementAccountingState> State { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.settlement-accounting-state.v1", PaymentsSettlementAccountingJsonContext.Default.SettlementAccountingStatePayload,
        SettlementAccountingStatePayload.From, static payload => payload.ToValue());
}

internal sealed record SettlementAccountingStatePayload(PaymentsIdentityPayload MovementId, decimal ExpectedMagnitude,
    string Unit, decimal? IncludedMagnitude, bool Excluded, bool Conflicted, bool AccountingAcknowledged, bool Residual,
    ulong Generation, PaymentsIdentityPayload LastEvidenceId)
{
    internal static SettlementAccountingStatePayload From(SettlementAccountingState value) => new(
        PaymentsIdentityPayload.From(value.MovementId), value.ExpectedMagnitude, value.Unit, value.IncludedMagnitude,
        value.Excluded, value.Conflicted, value.AccountingAcknowledged, value.Residual, value.Generation.Value,
        PaymentsIdentityPayload.From(value.LastEvidenceId));
    internal SettlementAccountingState ToValue() => SettlementAccountingState.Restore(MovementId.ToValue(), ExpectedMagnitude,
        Unit, IncludedMagnitude, Excluded, Conflicted, AccountingAcknowledged, Residual, OwnerGeneration.Create(Generation),
        LastEvidenceId.ToValue());
}

[JsonSerializable(typeof(SettlementAccountingStatePayload))]
internal sealed partial class PaymentsSettlementAccountingJsonContext : JsonSerializerContext;

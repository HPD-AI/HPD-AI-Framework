using System.Globalization;
using System.Text.Json.Serialization;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Entitlement;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides the closed AOT-safe codec for entitlement/restriction history projections.</summary>
public static class PaymentsEntitlementRestrictionFactCodecs
{
    /// <summary>Gets the exact entitlement/restriction state codec.</summary>
    public static PaymentsFactJsonCodec<EntitlementRestrictionState> State { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.entitlement-restriction-state.v1", PaymentsEntitlementRestrictionJsonContext.Default.EntitlementRestrictionStatePayload,
        EntitlementRestrictionStatePayload.From, static payload => payload.ToValue());
}

internal sealed record EntitlementRestrictionEntryPayload(string Kind, PaymentsIdentityPayload FactId, PaymentsIdentityPayload SubjectId,
    string Dimension, PaymentsIdentityPayload OwnerId, PaymentsIdentityPayload? PredecessorFactId, string EffectiveFrom,
    string? EffectiveTo, string RecordedAt, ulong Generation)
{
    internal static EntitlementRestrictionEntryPayload From(EntitlementRestrictionEntry value) => new(value.Kind,
        PaymentsIdentityPayload.From(value.FactId), PaymentsIdentityPayload.From(value.SubjectId), value.Dimension,
        PaymentsIdentityPayload.From(value.OwnerId), value.PredecessorFactId is { } predecessor ? PaymentsIdentityPayload.From(predecessor) : null,
        value.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture), value.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture),
        value.RecordedAt.ToString("O", CultureInfo.InvariantCulture), value.Generation.Value);
    internal EntitlementRestrictionEntry ToValue() => new(Kind, FactId.ToValue(), SubjectId.ToValue(), Dimension, OwnerId.ToValue(),
        PredecessorFactId?.ToValue(), Parse(EffectiveFrom), EffectiveTo is null ? null : Parse(EffectiveTo), Parse(RecordedAt), OwnerGeneration.Create(Generation));
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

internal sealed record EntitlementRestrictionStatePayload(PaymentsIdentityPayload SubjectId, ulong Generation,
    EntitlementRestrictionEntryPayload[] History)
{
    internal static EntitlementRestrictionStatePayload From(EntitlementRestrictionState value) => new(PaymentsIdentityPayload.From(value.SubjectId),
        value.Generation.Value, value.History.Select(EntitlementRestrictionEntryPayload.From).ToArray());
    internal EntitlementRestrictionState ToValue() => EntitlementRestrictionState.Restore(SubjectId.ToValue(), OwnerGeneration.Create(Generation),
        History.Select(static x => x.ToValue()).ToArray());
}

[JsonSerializable(typeof(EntitlementRestrictionStatePayload))]
internal sealed partial class PaymentsEntitlementRestrictionJsonContext : JsonSerializerContext;

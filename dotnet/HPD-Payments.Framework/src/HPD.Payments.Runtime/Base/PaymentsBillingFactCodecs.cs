using System.Text.Json.Serialization;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Billing;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides the closed AOT-safe codec for revision-bound billing manifests.</summary>
public static class PaymentsBillingFactCodecs
{
    /// <summary>Gets the exact billing-manifest codec.</summary>
    public static PaymentsFactJsonCodec<BillingManifest> Manifest { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.billing-manifest.v1", PaymentsBillingJsonContext.Default.BillingManifestPayload,
        BillingManifestPayload.From, static payload => payload.ToValue());
}

internal sealed record BillingManifestPayload(
    PaymentsIdentityPayload ManifestId,
    PaymentsIdentityPayload[] ObligationFactIds,
    HistoricalCutPayload SourceCut,
    RevisionPayload TaxRevision,
    RevisionPayload FxRevision,
    RevisionPayload RoundingRevision,
    int Closure,
    PaymentsDigestPayload Digest)
{
    internal static BillingManifestPayload From(BillingManifest value) => new(
        PaymentsIdentityPayload.From(value.ManifestId), value.ObligationFactIds.Select(PaymentsIdentityPayload.From).ToArray(),
        HistoricalCutPayload.From(value.SourceCut), RevisionPayload.From(value.TaxRevision), RevisionPayload.From(value.FxRevision),
        RevisionPayload.From(value.RoundingRevision), (int)value.Closure, PaymentsDigestPayload.From(value.Digest));

    internal BillingManifest ToValue() => new(ManifestId.ToValue(), ObligationFactIds.Select(static id => id.ToValue()),
        SourceCut.ToValue(), TaxRevision.ToValue(), FxRevision.ToValue(), RoundingRevision.ToValue(),
        (BillingClosureKind)Closure, Digest.ToValue());
}

[JsonSerializable(typeof(BillingManifestPayload))]
internal sealed partial class PaymentsBillingJsonContext : JsonSerializerContext;

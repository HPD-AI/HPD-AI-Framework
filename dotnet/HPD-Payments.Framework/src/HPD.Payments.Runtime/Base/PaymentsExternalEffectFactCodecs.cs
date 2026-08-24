using System.Text.Json.Serialization;
using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Runtime.ExternalEffects;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides the closed AOT-safe codec for external-effect knowledge states.</summary>
public static class PaymentsExternalEffectFactCodecs
{
    /// <summary>Gets the exact external-effect protocol-state codec.</summary>
    public static PaymentsFactJsonCodec<ExternalEffectProtocolState> State { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.external-effect-state.v1", PaymentsExternalEffectJsonContext.Default.ExternalEffectStatePayload,
        ExternalEffectStatePayload.From, static payload => payload.ToValue());
}

internal sealed record ExternalEffectOperationPayload(PaymentsIdentityPayload OperationId, PaymentsIdentityPayload AttemptId,
    PaymentsIdentityPayload ProviderAccountId, string IdempotencyKey, PaymentsDigestPayload RequestDigest,
    RevisionPayload CredentialRevision, RevisionPayload ConfigurationRevision)
{
    internal static ExternalEffectOperationPayload From(ExternalEffectOperation value) => new(
        PaymentsIdentityPayload.From(value.OperationId), PaymentsIdentityPayload.From(value.AttemptId),
        PaymentsIdentityPayload.From(value.ProviderAccountId), value.IdempotencyKey, PaymentsDigestPayload.From(value.RequestDigest),
        RevisionPayload.From(value.CredentialRevision), RevisionPayload.From(value.ConfigurationRevision));
    internal ExternalEffectOperation ToValue() => new(OperationId.ToValue(), AttemptId.ToValue(), ProviderAccountId.ToValue(),
        IdempotencyKey, RequestDigest.ToValue(), CredentialRevision.ToValue(), ConfigurationRevision.ToValue());
}

internal sealed record ExternalEffectStatePayload(ExternalEffectOperationPayload Operation, int State, PaymentsDigestPayload LatestFactDigest)
{
    internal static ExternalEffectStatePayload From(ExternalEffectProtocolState value) => new(
        ExternalEffectOperationPayload.From(value.Operation), (int)value.State, PaymentsDigestPayload.From(value.LatestFactDigest));
    internal ExternalEffectProtocolState ToValue() => ExternalEffectProtocolState.Restore(Operation.ToValue(),
        (ExternalEffectState)State, LatestFactDigest.ToValue());
}

[JsonSerializable(typeof(ExternalEffectStatePayload))]
internal sealed partial class PaymentsExternalEffectJsonContext : JsonSerializerContext;

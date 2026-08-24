using System.Text.Json.Serialization;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;

namespace HPD.Payments.Runtime.Base;

internal sealed record PaymentsIdentityPayload(
    string Tenant, string Environment, string ScopeAuthority, string Namespace, string Kind, string LocalId, string? Provider, string? Account)
{
    internal static PaymentsIdentityPayload From(SemanticId value) => new(value.Scope.Tenant, value.Scope.Environment, value.Scope.Authority,
        value.Namespace, value.Kind, value.LocalId, value.Provider, value.Account);
    internal SemanticId ToValue() => SemanticId.Create(ScopeId.Create(Tenant, Environment, ScopeAuthority), Namespace, Kind, LocalId, Provider, Account);
}

internal sealed record PaymentsOwnerPayload(int Authority, PaymentsIdentityPayload Subject, ulong Generation)
{
    internal static PaymentsOwnerPayload From(OwnerReference value) => new((int)value.Authority, PaymentsIdentityPayload.From(value.SubjectId), value.Generation.Value);
    internal OwnerReference ToValue() => new((FrozenAuthority)Authority, Subject.ToValue(), OwnerGeneration.Create(Generation));
}

internal sealed record PaymentsDigestPayload(
    string Discriminator, ushort Major, ushort Minor, string FieldSet, string Normalization, string TimeGrammar, string CollectionOrder,
    string AlgorithmKeyId, string Algorithm, string Bytes)
{
    internal static PaymentsDigestPayload From(CanonicalDigest value) => new(value.Profile.SemanticDiscriminator, value.Profile.SemanticVersion.Major,
        value.Profile.SemanticVersion.Minor, value.Profile.FieldSet, value.Profile.Normalization, value.Profile.NumericTimeGrammar,
        value.Profile.CollectionOrder, value.Profile.AlgorithmKeyId, value.Algorithm, Convert.ToBase64String(value.CopyBytes()));
    internal CanonicalDigest ToValue() => new(new CanonicalDigestProfileId(Discriminator, ContractVersion.Create(Major, Minor), FieldSet,
        Normalization, TimeGrammar, CollectionOrder, AlgorithmKeyId), Algorithm, Convert.FromBase64String(Bytes));
}

internal sealed record PaymentsRelationPayload(
    PaymentsIdentityPayload RelationId, int Kind, PaymentsOwnerPayload Source, PaymentsOwnerPayload Target, string RevisionKind, ulong Revision)
{
    internal static PaymentsRelationPayload From(SupportingRelation value) => new(PaymentsIdentityPayload.From(value.RelationId), (int)value.Kind,
        PaymentsOwnerPayload.From(value.Source), PaymentsOwnerPayload.From(value.Target), value.RelationRevision.Kind, value.RelationRevision.Value);
    internal SupportingRelation ToValue() => new(RelationId.ToValue(), (SupportingRelationKind)Kind, Source.ToValue(), Target.ToValue(),
        HPD.Payments.Primitives.Identity.Revision.Create(RevisionKind, Revision));
}

internal sealed record PaymentsContinuationPayload(PaymentsOwnerPayload Owner, PaymentsIdentityPayload ContinuationId, PaymentsDigestPayload Digest)
{
    internal static PaymentsContinuationPayload From(ContinuationDeclaration value) => new(PaymentsOwnerPayload.From(value.Owner),
        PaymentsIdentityPayload.From(value.ContinuationId), PaymentsDigestPayload.From(value.Digest));
    internal ContinuationDeclaration ToValue() => new(Owner.ToValue(), ContinuationId.ToValue(), Digest.ToValue());
}

internal sealed record PaymentsCustodyPayload(
    PaymentsIdentityPayload InstanceId, PaymentsOwnerPayload Subject, PaymentsIdentityPayload ControllerId, ulong InventoryGeneration,
    int Classification, int Retention, string PolicyKind, ulong PolicyRevision, string HoldKind, ulong HoldRevision,
    int State, int TimeKind, string ObservedAt)
{
    internal static PaymentsCustodyPayload From(CustodyInstance value) => new(PaymentsIdentityPayload.From(value.InstanceId),
        PaymentsOwnerPayload.From(value.Subject), PaymentsIdentityPayload.From(value.ControllerId), value.InventoryGeneration.Value,
        (int)value.Classification.Classification, (int)value.Classification.Retention, value.PolicyRevision.Kind, value.PolicyRevision.Value,
        value.HoldRevision.Kind, value.HoldRevision.Value, (int)value.State, (int)value.ObservedAt.Kind, value.ObservedAt.Value.ToString("O"));
    internal CustodyInstance ToValue() => new(InstanceId.ToValue(), Subject.ToValue(), ControllerId.ToValue(), OwnerGeneration.Create(InventoryGeneration),
        ClassificationMark.Create((DataClassification)Classification, (RetentionKind)Retention), Revision.Create(PolicyKind, PolicyRevision),
        Revision.Create(HoldKind, HoldRevision), (CustodyState)State,
        NamedTime.Create((HPD.Payments.Primitives.Time.TimeKind)TimeKind, DateTimeOffset.Parse(ObservedAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)));
}

[JsonSerializable(typeof(PaymentsRelationPayload))]
[JsonSerializable(typeof(PaymentsContinuationPayload))]
[JsonSerializable(typeof(PaymentsCustodyPayload))]
internal sealed partial class PaymentsSupportingPayloadJsonContext : JsonSerializerContext;

using System.Text.Json.Serialization;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Base;

namespace HPD.Payments.Profiles.Distributed;

/// <summary>Provides the closed AOT-safe codec for storage-neutral cutover projections.</summary>
public static class DistributedCutoverFactCodecs
{
    /// <summary>Gets the exact distributed cutover projection codec.</summary>
    public static PaymentsFactJsonCodec<DistributedCutoverProtocol> State { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.distributed-cutover-state.v1", DistributedCutoverJsonContext.Default.DistributedCutoverPayload,
        DistributedCutoverPayload.From, static payload => payload.ToValue());
}

internal sealed record CutoverIdentityPayload(string Tenant, string Environment, string Authority, string Namespace,
    string Kind, string LocalId, string? Provider, string? Account)
{
    internal static CutoverIdentityPayload From(SemanticId value) => new(value.Scope.Tenant, value.Scope.Environment,
        value.Scope.Authority, value.Namespace, value.Kind, value.LocalId, value.Provider, value.Account);
    internal SemanticId ToValue() => SemanticId.Create(ScopeId.Create(Tenant, Environment, Authority), Namespace, Kind, LocalId, Provider, Account);
}

internal sealed record DistributedCutoverPayload(CutoverIdentityPayload CutoverId, CutoverIdentityPayload SourceProfileId,
    CutoverIdentityPayload TargetProfileId, ulong ComparedThrough, int State, string? ResidueCode)
{
    internal static DistributedCutoverPayload From(DistributedCutoverProtocol value) => new(CutoverIdentityPayload.From(value.CutoverId),
        CutoverIdentityPayload.From(value.SourceProfileId), CutoverIdentityPayload.From(value.TargetProfileId), value.ComparedThrough.Value,
        (int)value.State, value.ResidueCode);
    internal DistributedCutoverProtocol ToValue() => DistributedCutoverProtocol.Restore(CutoverId.ToValue(), SourceProfileId.ToValue(),
        TargetProfileId.ToValue(), ComparedThrough == 0 ? default : OwnerGeneration.Create(ComparedThrough),
        (DistributedCutoverState)State, ResidueCode);
}

[JsonSerializable(typeof(DistributedCutoverPayload))]
internal sealed partial class DistributedCutoverJsonContext : JsonSerializerContext;

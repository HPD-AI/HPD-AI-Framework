using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Routing;

internal sealed record RouteCandidateV1
{
    internal RouteCandidateV1(ProviderId providerId,BoundedAscii role,Hash256 capabilityFingerprint,Hash256 configurationFingerprint,ushort priority,bool available)
    {if(!providerId.IsValid||!role.IsValid||capabilityFingerprint==default||configurationFingerprint==default||priority==0)throw new ArgumentException("Route candidate is invalid.");ProviderId=providerId;Role=role;CapabilityFingerprint=capabilityFingerprint;ConfigurationFingerprint=configurationFingerprint;Priority=priority;Available=available;}
    internal ProviderId ProviderId{get;}internal BoundedAscii Role{get;}internal Hash256 CapabilityFingerprint{get;}internal Hash256 ConfigurationFingerprint{get;}internal ushort Priority{get;}internal bool Available{get;}
}

internal sealed record RouteCompileRequestV1(OperationId OperationId,ExpectedAuthorityVectorV1 Authority,
    Hash256 CatalogFingerprint,BoundedAscii RequiredRole,Hash256 RequiredCapabilityFingerprint,
    IReadOnlyList<RouteCandidateV1> Candidates);
internal sealed record CompiledRouteV1(OperationId OperationId,ProviderId ProviderId,RouteGenerationId ProposedGeneration,
    ExpectedAuthorityVectorV1 Authority,Hash256 CatalogFingerprint,Hash256 CandidateFingerprint);

internal abstract record RouteCompileResultV1
{
    private RouteCompileResultV1(){}
    internal sealed record Compiled(CompiledRouteV1 Route):RouteCompileResultV1;
    internal sealed record Unavailable(BoundedAscii SafeCode):RouteCompileResultV1;
    internal sealed record Invalid(BoundedAscii SafeCode):RouteCompileResultV1;
}

internal static class RouteCompilerV1
{
    internal static RouteCompileResultV1 Compile(RouteCompileRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if(!request.OperationId.IsValid||request.Authority is null||request.CatalogFingerprint==default||!request.RequiredRole.IsValid||
            request.RequiredCapabilityFingerprint==default||request.Candidates is null||request.Candidates.Count is 0 or >256)
            return new RouteCompileResultV1.Invalid(new BoundedAscii("route-request-invalid"));
        if(request.Candidates.Select(static x=>x.ProviderId).Distinct().Count()!=request.Candidates.Count)
            return new RouteCompileResultV1.Invalid(new BoundedAscii("route-candidate-duplicate"));
        var eligible=request.Candidates.Where(candidate=>candidate.Available&&candidate.Role==request.RequiredRole&&
            candidate.CapabilityFingerprint==request.RequiredCapabilityFingerprint)
            .OrderBy(static candidate=>candidate.Priority).ThenBy(static candidate=>Key(candidate.ProviderId),StringComparer.Ordinal).ToArray();
        if(eligible.Length==0)return new RouteCompileResultV1.Unavailable(new BoundedAscii("route-unavailable"));
        var selected=eligible[0];
        return new RouteCompileResultV1.Compiled(new(request.OperationId,selected.ProviderId,
            DeriveGeneration(request,selected),request.Authority,request.CatalogFingerprint,selected.ConfigurationFingerprint));
    }
    private static RouteGenerationId DeriveGeneration(RouteCompileRequestV1 request,RouteCandidateV1 selected)
    {using var hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);hash.AppendData("hpd-route-generation-v1\0"u8);Span<byte>b=stackalloc byte[16];request.OperationId.TryWriteBytes(b);hash.AppendData(b);selected.ProviderId.TryWriteBytes(b);hash.AppendData(b);Span<byte>h=stackalloc byte[32];request.CatalogFingerprint.TryWriteBytes(h);hash.AppendData(h);selected.ConfigurationFingerprint.TryWriteBytes(h);hash.AppendData(h);return RouteGenerationId.FromValue(StableId128.FromBytes(hash.GetHashAndReset().AsSpan(0,16)));}
    private static string Key(ProviderId id){Span<byte>b=stackalloc byte[16];if(!id.TryWriteBytes(b))throw new InvalidOperationException();return Convert.ToHexString(b);}
}

internal enum RoutePreparationPhaseV1:ushort{None=1,RequestAdmitted=2,OwnerClaimed=3,CutoverAuthorized=4}
internal abstract record RoutePreparationCommandV1
{
    private protected RoutePreparationCommandV1(OperationId operationId,ulong expectedRevision){if(!operationId.IsValid)throw new ArgumentException();OperationId=operationId;ExpectedRevision=expectedRevision;}
    internal OperationId OperationId{get;}internal ulong ExpectedRevision{get;}
    internal sealed record Admit(OperationId O,ulong R):RoutePreparationCommandV1(O,R);
    internal sealed record ClaimOwner(OperationId O,ulong R,OwnerSliceId Owner):RoutePreparationCommandV1(O,R);
    internal sealed record AuthorizeCutover(OperationId O,ulong R,bool ProviderPrepared):RoutePreparationCommandV1(O,R);
    internal sealed record CommitCutover(OperationId O,ulong R):RoutePreparationCommandV1(O,R);
}
internal sealed record RoutePreparationSnapshotV1(ulong Revision,RoutePreparationPhaseV1 Phase,OwnerSliceId? PreparationOwner);
internal sealed record RoutePreparationReceiptV1(RoutePreparationCommandV1 Command,RoutePreparationSnapshotV1 Snapshot);
internal sealed class RoutePreparationStateV1
{
    private readonly ReadOnlyDictionary<OperationId,RoutePreparationReceiptV1> _receipts;
    internal RoutePreparationStateV1(CompiledRouteV1 route,RoutePreparationSnapshotV1 snapshot,IDictionary<OperationId,RoutePreparationReceiptV1>? receipts=null)
    {Route=route??throw new ArgumentNullException(nameof(route));Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));_receipts=new(receipts is null?new Dictionary<OperationId,RoutePreparationReceiptV1>():new Dictionary<OperationId,RoutePreparationReceiptV1>(receipts));}
    internal CompiledRouteV1 Route{get;}internal RoutePreparationSnapshotV1 Snapshot{get;}internal IReadOnlyDictionary<OperationId,RoutePreparationReceiptV1> Receipts=>_receipts;
}
internal abstract record RoutePreparationResultV1
{
    private RoutePreparationResultV1(){}internal sealed record Applied(RoutePreparationStateV1 State,RoutePreparationReceiptV1 Receipt):RoutePreparationResultV1;internal sealed record Duplicate(RoutePreparationStateV1 State,RoutePreparationReceiptV1 Receipt):RoutePreparationResultV1;internal sealed record CutoverUnavailable(RoutePreparationStateV1 State,BoundedAscii SafeCode):RoutePreparationResultV1;internal sealed record Rejected(RoutePreparationStateV1 State,BoundedAscii SafeCode):RoutePreparationResultV1;
}
internal static class RoutePreparationSupervisorV1
{
    internal static RoutePreparationStateV1 Create(CompiledRouteV1 route)=>new(route,new(0,RoutePreparationPhaseV1.None,null));
    internal static RoutePreparationResultV1 Apply(RoutePreparationStateV1 state,RoutePreparationCommandV1 command,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new RoutePreparationResultV1.Duplicate(state,prior):Reject(state,"route-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"route-receipt-capacity-refused");if(command.ExpectedRevision!=state.Snapshot.Revision)return Reject(state,"route-revision-conflict");
        if(command is RoutePreparationCommandV1.CommitCutover)return new RoutePreparationResultV1.CutoverUnavailable(state,new BoundedAscii("route-cutover-unavailable"));
        var s=state.Snapshot;RoutePreparationSnapshotV1? next=command switch
        {RoutePreparationCommandV1.Admit when s.Phase==RoutePreparationPhaseV1.None=>new(s.Revision+1,RoutePreparationPhaseV1.RequestAdmitted,null),
         RoutePreparationCommandV1.ClaimOwner claim when s.Phase==RoutePreparationPhaseV1.RequestAdmitted&&claim.Owner==OwnerSliceId.S5=>new(s.Revision+1,RoutePreparationPhaseV1.OwnerClaimed,claim.Owner),
         RoutePreparationCommandV1.AuthorizeCutover authorize when s.Phase==RoutePreparationPhaseV1.OwnerClaimed&&authorize.ProviderPrepared=>new(s.Revision+1,RoutePreparationPhaseV1.CutoverAuthorized,s.PreparationOwner),_=>null};
        if(next is null)return Reject(state,"route-preparation-invalid");var receipt=new RoutePreparationReceiptV1(command,next);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);return new RoutePreparationResultV1.Applied(new(state.Route,next,receipts),receipt);
    }
    private static RoutePreparationResultV1.Rejected Reject(RoutePreparationStateV1 state,string code)=>new(state,new BoundedAscii(code));
}

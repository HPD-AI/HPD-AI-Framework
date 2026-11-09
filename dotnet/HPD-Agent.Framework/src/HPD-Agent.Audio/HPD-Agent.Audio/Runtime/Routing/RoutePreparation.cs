using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Routing;

internal sealed record RouteCandidate
{
    internal RouteCandidate(ProviderId providerId,BoundedAscii role,Hash256 capabilityFingerprint,Hash256 configurationFingerprint,ushort priority,bool available)
    {if(!providerId.IsValid||!role.IsValid||capabilityFingerprint==default||configurationFingerprint==default||priority==0)throw new ArgumentException("Route candidate is invalid.");ProviderId=providerId;Role=role;CapabilityFingerprint=capabilityFingerprint;ConfigurationFingerprint=configurationFingerprint;Priority=priority;Available=available;}
    internal ProviderId ProviderId{get;}internal BoundedAscii Role{get;}internal Hash256 CapabilityFingerprint{get;}internal Hash256 ConfigurationFingerprint{get;}internal ushort Priority{get;}internal bool Available{get;}
}

internal sealed record RouteCompileRequest(OperationId OperationId,ExpectedAuthorityVectorV1 Authority,
    Hash256 CatalogFingerprint,BoundedAscii RequiredRole,Hash256 RequiredCapabilityFingerprint,
    IReadOnlyList<RouteCandidate> Candidates);
internal sealed record CompiledRoute(OperationId OperationId,ProviderId ProviderId,RouteGenerationId ProposedGeneration,
    ExpectedAuthorityVectorV1 Authority,Hash256 CatalogFingerprint,Hash256 CandidateFingerprint);

internal abstract record RouteCompileResult
{
    private RouteCompileResult(){}
    internal sealed record Compiled(CompiledRoute Route):RouteCompileResult;
    internal sealed record Unavailable(BoundedAscii SafeCode):RouteCompileResult;
    internal sealed record Invalid(BoundedAscii SafeCode):RouteCompileResult;
}

internal static class RouteCompiler
{
    internal static RouteCompileResult Compile(RouteCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if(!request.OperationId.IsValid||request.Authority is null||request.CatalogFingerprint==default||!request.RequiredRole.IsValid||
            request.RequiredCapabilityFingerprint==default||request.Candidates is null||request.Candidates.Count is 0 or >256)
            return new RouteCompileResult.Invalid(new BoundedAscii("route-request-invalid"));
        if(request.Candidates.Select(static x=>x.ProviderId).Distinct().Count()!=request.Candidates.Count)
            return new RouteCompileResult.Invalid(new BoundedAscii("route-candidate-duplicate"));
        var eligible=request.Candidates.Where(candidate=>candidate.Available&&candidate.Role==request.RequiredRole&&
            candidate.CapabilityFingerprint==request.RequiredCapabilityFingerprint)
            .OrderBy(static candidate=>candidate.Priority).ThenBy(static candidate=>Key(candidate.ProviderId),StringComparer.Ordinal).ToArray();
        if(eligible.Length==0)return new RouteCompileResult.Unavailable(new BoundedAscii("route-unavailable"));
        var selected=eligible[0];
        return new RouteCompileResult.Compiled(new(request.OperationId,selected.ProviderId,
            DeriveGeneration(request,selected),request.Authority,request.CatalogFingerprint,selected.ConfigurationFingerprint));
    }
    private static RouteGenerationId DeriveGeneration(RouteCompileRequest request,RouteCandidate selected)
    {using var hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);hash.AppendData("hpd-route-generation-v1\0"u8);Span<byte>b=stackalloc byte[16];request.OperationId.TryWriteBytes(b);hash.AppendData(b);selected.ProviderId.TryWriteBytes(b);hash.AppendData(b);Span<byte>h=stackalloc byte[32];request.CatalogFingerprint.TryWriteBytes(h);hash.AppendData(h);selected.ConfigurationFingerprint.TryWriteBytes(h);hash.AppendData(h);return RouteGenerationId.FromValue(StableId128.FromBytes(hash.GetHashAndReset().AsSpan(0,16)));}
    private static string Key(ProviderId id){Span<byte>b=stackalloc byte[16];if(!id.TryWriteBytes(b))throw new InvalidOperationException();return Convert.ToHexString(b);}
}

internal enum RoutePreparationPhase:ushort{None=1,RequestAdmitted=2,OwnerClaimed=3,CutoverAuthorized=4}
internal abstract record RoutePreparationCommand
{
    private protected RoutePreparationCommand(OperationId operationId,ulong expectedRevision){if(!operationId.IsValid)throw new ArgumentException();OperationId=operationId;ExpectedRevision=expectedRevision;}
    internal OperationId OperationId{get;}internal ulong ExpectedRevision{get;}
    internal sealed record Admit(OperationId O,ulong R):RoutePreparationCommand(O,R);
    internal sealed record ClaimOwner(OperationId O,ulong R,OwnerSliceId Owner):RoutePreparationCommand(O,R);
    internal sealed record AuthorizeCutover(OperationId O,ulong R,bool ProviderPrepared):RoutePreparationCommand(O,R);
    internal sealed record CommitCutover(OperationId O,ulong R):RoutePreparationCommand(O,R);
}
internal sealed record RoutePreparationSnapshot(ulong Revision,RoutePreparationPhase Phase,OwnerSliceId? PreparationOwner);
internal sealed record RoutePreparationReceipt(RoutePreparationCommand Command,RoutePreparationSnapshot Snapshot);
internal sealed class RoutePreparationState
{
    private readonly ReadOnlyDictionary<OperationId,RoutePreparationReceipt> _receipts;
    internal RoutePreparationState(CompiledRoute route,RoutePreparationSnapshot snapshot,IDictionary<OperationId,RoutePreparationReceipt>? receipts=null)
    {Route=route??throw new ArgumentNullException(nameof(route));Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));_receipts=new(receipts is null?new Dictionary<OperationId,RoutePreparationReceipt>():new Dictionary<OperationId,RoutePreparationReceipt>(receipts));}
    internal CompiledRoute Route{get;}internal RoutePreparationSnapshot Snapshot{get;}internal IReadOnlyDictionary<OperationId,RoutePreparationReceipt> Receipts=>_receipts;
}
internal abstract record RoutePreparationResult
{
    private RoutePreparationResult(){}internal sealed record Applied(RoutePreparationState State,RoutePreparationReceipt Receipt):RoutePreparationResult;internal sealed record Duplicate(RoutePreparationState State,RoutePreparationReceipt Receipt):RoutePreparationResult;internal sealed record CutoverUnavailable(RoutePreparationState State,BoundedAscii SafeCode):RoutePreparationResult;internal sealed record Rejected(RoutePreparationState State,BoundedAscii SafeCode):RoutePreparationResult;
}
internal static class RoutePreparationSupervisor
{
    internal static RoutePreparationState Create(CompiledRoute route)=>new(route,new(0,RoutePreparationPhase.None,null));
    internal static RoutePreparationResult Apply(RoutePreparationState state,RoutePreparationCommand command,ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(command);if(maximumReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if(state.Receipts.TryGetValue(command.OperationId,out var prior))return prior.Command==command?new RoutePreparationResult.Duplicate(state,prior):Reject(state,"route-operation-contradiction");
        if(state.Receipts.Count>=maximumReceipts)return Reject(state,"route-receipt-capacity-refused");if(command.ExpectedRevision!=state.Snapshot.Revision)return Reject(state,"route-revision-conflict");
        if(command is RoutePreparationCommand.CommitCutover)return new RoutePreparationResult.CutoverUnavailable(state,new BoundedAscii("route-cutover-unavailable"));
        var s=state.Snapshot;RoutePreparationSnapshot? next=command switch
        {RoutePreparationCommand.Admit when s.Phase==RoutePreparationPhase.None=>new(s.Revision+1,RoutePreparationPhase.RequestAdmitted,null),
         RoutePreparationCommand.ClaimOwner claim when s.Phase==RoutePreparationPhase.RequestAdmitted&&claim.Owner==OwnerSliceId.S5=>new(s.Revision+1,RoutePreparationPhase.OwnerClaimed,claim.Owner),
         RoutePreparationCommand.AuthorizeCutover authorize when s.Phase==RoutePreparationPhase.OwnerClaimed&&authorize.ProviderPrepared=>new(s.Revision+1,RoutePreparationPhase.CutoverAuthorized,s.PreparationOwner),_=>null};
        if(next is null)return Reject(state,"route-preparation-invalid");var receipt=new RoutePreparationReceipt(command,next);var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(command.OperationId,receipt);return new RoutePreparationResult.Applied(new(state.Route,next,receipts),receipt);
    }
    private static RoutePreparationResult.Rejected Reject(RoutePreparationState state,string code)=>new(state,new BoundedAscii(code));
}

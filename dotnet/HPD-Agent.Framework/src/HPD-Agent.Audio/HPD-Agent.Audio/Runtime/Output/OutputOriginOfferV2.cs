using System.Collections.ObjectModel;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Output;

internal sealed record OutputOriginEvidenceV2
{
    internal OutputOriginEvidenceV2(TurnDecisionFinalizedV1 decision, ProviderParticipantSnapshotV1 provider)
    {
        Decision=decision??throw new ArgumentNullException(nameof(decision));Provider=provider??throw new ArgumentNullException(nameof(provider));
        if(provider.Phase!=ProviderParticipantPhaseV1.Effective||provider.Plan is null||provider.Inflight!=0)
            throw new ArgumentException("Output origin requires an effective idle provider participant.");
    }
    internal TurnDecisionFinalizedV1 Decision{get;}
    internal ProviderParticipantSnapshotV1 Provider{get;}
}

internal sealed record OutputOfferV2
{
    internal OutputOfferV2(OperationId operationId,OutputGenerationId outputGeneration,long maximumUnits,Hash256 contentFingerprint,OutputOriginEvidenceV2 origin)
    {
        if(!operationId.IsValid||!outputGeneration.IsValid||maximumUnits<=0||contentFingerprint==default)throw new ArgumentException("Output offer is invalid.");
        OperationId=operationId;OutputGeneration=outputGeneration;MaximumUnits=maximumUnits;ContentFingerprint=contentFingerprint;Origin=origin??throw new ArgumentNullException(nameof(origin));
    }
    internal OperationId OperationId{get;} internal OutputGenerationId OutputGeneration{get;} internal long MaximumUnits{get;} internal Hash256 ContentFingerprint{get;} internal OutputOriginEvidenceV2 Origin{get;}
}

internal sealed record AcceptedOutputOfferV2(OutputOfferV2 Offer,OutputPlanV2 Plan);

internal abstract record OutputOfferResultV2
{
    private OutputOfferResultV2(){}
    internal sealed record Accepted(OutputOfferAcceptanceStateV2 State,AcceptedOutputOfferV2 Receipt,InMemoryOutputControllerV2 Controller):OutputOfferResultV2;
    internal sealed record Duplicate(OutputOfferAcceptanceStateV2 State,AcceptedOutputOfferV2 Receipt):OutputOfferResultV2;
    internal sealed record Rejected(OutputOfferAcceptanceStateV2 State,BoundedAscii SafeCode):OutputOfferResultV2;
}

internal sealed class OutputOfferAcceptanceStateV2
{
    private readonly ReadOnlyDictionary<OperationId,AcceptedOutputOfferV2> _receipts;
    internal OutputOfferAcceptanceStateV2(IDictionary<OperationId,AcceptedOutputOfferV2>? receipts=null)=>_receipts=new(receipts is null?new Dictionary<OperationId,AcceptedOutputOfferV2>():new Dictionary<OperationId,AcceptedOutputOfferV2>(receipts));
    internal IReadOnlyDictionary<OperationId,AcceptedOutputOfferV2> Receipts=>_receipts;
}

internal static class OutputOfferCoordinatorV2
{
    internal static OutputOfferResultV2 Accept(OutputOfferAcceptanceStateV2 state,OutputOfferV2 offer,ushort maximumOffers,ushort maximumOutputReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(offer);
        if(maximumOffers==0||maximumOutputReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumOffers));
        if(state.Receipts.TryGetValue(offer.OperationId,out var prior))return prior.Offer==offer?new OutputOfferResultV2.Duplicate(state,prior):Reject(state,"output-offer-contradiction");
        if(state.Receipts.Count>=maximumOffers)return Reject(state,"output-offer-capacity-refused");
        var decision=offer.Origin.Decision;var provider=offer.Origin.Provider;var plan=provider.Plan!;
        if(decision.Authority.Session!=decision.SourcePosition.Session||plan.Authority.Session!=decision.Authority.Session)return Reject(state,"output-origin-session-mismatch");
        if(!SameAuthority(plan.Authority,decision.Authority))return Reject(state,"output-origin-authority-mismatch");
        if(!Axis(decision.Authority,static x=>x is AuthorityAxisValueV1.Turn,out _))return Reject(state,"output-origin-turn-missing");
        if(!Axis(decision.Authority,static x=>x is AuthorityAxisValueV1.Provider,out var providerAxis)||((AuthorityAxisValueV1.Provider)providerAxis!).Value!=plan.Generation)return Reject(state,"output-origin-provider-stale");
        if(!Axis(decision.Authority,static x=>x is AuthorityAxisValueV1.Route,out var routeAxis)||((AuthorityAxisValueV1.Route)routeAxis!).Value!=plan.RouteGeneration)return Reject(state,"output-origin-route-stale");
        if(!Axis(decision.Authority,static x=>x is AuthorityAxisValueV1.Output,out var outputAxis)||((AuthorityAxisValueV1.Output)outputAxis!).Value!=offer.OutputGeneration)return Reject(state,"output-origin-output-stale");
        var outputPlan=new OutputPlanV2(offer.OperationId,offer.OutputGeneration,decision.Authority,offer.MaximumUnits);var receipt=new AcceptedOutputOfferV2(offer,outputPlan);
        var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(offer.OperationId,receipt);var next=new OutputOfferAcceptanceStateV2(receipts);
        return new OutputOfferResultV2.Accepted(next,receipt,new InMemoryOutputControllerV2(outputPlan,maximumOutputReceipts));
    }
    private static bool Axis(ExpectedAuthorityVectorV1 authority,Func<AuthorityAxisValueV1,bool> predicate,out AuthorityAxisValueV1? axis)
    {axis=authority.Axes.Select(static x=>x.Value).SingleOrDefault(predicate);return axis is not null;}
    private static bool SameAuthority(ExpectedAuthorityVectorV1 left,ExpectedAuthorityVectorV1 right)=>left.Session==right.Session&&left.Axes.SequenceEqual(right.Axes);
    private static OutputOfferResultV2.Rejected Reject(OutputOfferAcceptanceStateV2 state,string code)=>new(state,new BoundedAscii(code));
}

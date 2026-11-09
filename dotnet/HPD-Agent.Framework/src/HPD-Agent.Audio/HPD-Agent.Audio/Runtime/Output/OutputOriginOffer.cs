using System.Collections.ObjectModel;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Output;

internal sealed record OutputOriginEvidence
{
    internal OutputOriginEvidence(TurnDecisionFinalized decision, ProviderParticipantSnapshot provider)
    {
        Decision=decision??throw new ArgumentNullException(nameof(decision));Provider=provider??throw new ArgumentNullException(nameof(provider));
        if(provider.Phase!=ProviderParticipantPhase.Effective||provider.Plan is null||provider.Inflight!=0)
            throw new ArgumentException("Output origin requires an effective idle provider participant.");
    }
    internal TurnDecisionFinalized Decision{get;}
    internal ProviderParticipantSnapshot Provider{get;}
}

internal sealed record OutputOffer
{
    internal OutputOffer(OperationId operationId,OutputGenerationId outputGeneration,long maximumUnits,Hash256 contentFingerprint,OutputOriginEvidence origin)
    {
        if(!operationId.IsValid||!outputGeneration.IsValid||maximumUnits<=0||contentFingerprint==default)throw new ArgumentException("Output offer is invalid.");
        OperationId=operationId;OutputGeneration=outputGeneration;MaximumUnits=maximumUnits;ContentFingerprint=contentFingerprint;Origin=origin??throw new ArgumentNullException(nameof(origin));
    }
    internal OperationId OperationId{get;} internal OutputGenerationId OutputGeneration{get;} internal long MaximumUnits{get;} internal Hash256 ContentFingerprint{get;} internal OutputOriginEvidence Origin{get;}
}

internal sealed record AcceptedOutputOffer(OutputOffer Offer,OutputPlan Plan);

internal abstract record OutputOfferResult
{
    private OutputOfferResult(){}
    internal sealed record Accepted(OutputOfferAcceptanceState State,AcceptedOutputOffer Receipt,InMemoryOutputController Controller):OutputOfferResult;
    internal sealed record Duplicate(OutputOfferAcceptanceState State,AcceptedOutputOffer Receipt):OutputOfferResult;
    internal sealed record Rejected(OutputOfferAcceptanceState State,BoundedAscii SafeCode):OutputOfferResult;
}

internal sealed class OutputOfferAcceptanceState
{
    private readonly ReadOnlyDictionary<OperationId,AcceptedOutputOffer> _receipts;
    internal OutputOfferAcceptanceState(IDictionary<OperationId,AcceptedOutputOffer>? receipts=null)=>_receipts=new(receipts is null?new Dictionary<OperationId,AcceptedOutputOffer>():new Dictionary<OperationId,AcceptedOutputOffer>(receipts));
    internal IReadOnlyDictionary<OperationId,AcceptedOutputOffer> Receipts=>_receipts;
}

internal static class OutputOfferCoordinator
{
    internal static OutputOfferResult Accept(OutputOfferAcceptanceState state,OutputOffer offer,ushort maximumOffers,ushort maximumOutputReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(offer);
        if(maximumOffers==0||maximumOutputReceipts==0)throw new ArgumentOutOfRangeException(nameof(maximumOffers));
        if(state.Receipts.TryGetValue(offer.OperationId,out var prior))return prior.Offer==offer?new OutputOfferResult.Duplicate(state,prior):Reject(state,"output-offer-contradiction");
        if(state.Receipts.Count>=maximumOffers)return Reject(state,"output-offer-capacity-refused");
        var decision=offer.Origin.Decision;var provider=offer.Origin.Provider;var plan=provider.Plan!;
        if(decision.Authority.Session!=decision.SourcePosition.Session||plan.Authority.Session!=decision.Authority.Session)return Reject(state,"output-origin-session-mismatch");
        if(!SameAuthority(plan.Authority,decision.Authority))return Reject(state,"output-origin-authority-mismatch");
        if(!Axis(decision.Authority,static x=>x is AuthorityAxisValueV1.Turn,out _))return Reject(state,"output-origin-turn-missing");
        if(!Axis(decision.Authority,static x=>x is AuthorityAxisValueV1.Provider,out var providerAxis)||((AuthorityAxisValueV1.Provider)providerAxis!).Value!=plan.Generation)return Reject(state,"output-origin-provider-stale");
        if(!Axis(decision.Authority,static x=>x is AuthorityAxisValueV1.Route,out var routeAxis)||((AuthorityAxisValueV1.Route)routeAxis!).Value!=plan.RouteGeneration)return Reject(state,"output-origin-route-stale");
        if(!Axis(decision.Authority,static x=>x is AuthorityAxisValueV1.Output,out var outputAxis)||((AuthorityAxisValueV1.Output)outputAxis!).Value!=offer.OutputGeneration)return Reject(state,"output-origin-output-stale");
        var outputPlan=new OutputPlan(offer.OperationId,offer.OutputGeneration,decision.Authority,offer.MaximumUnits);var receipt=new AcceptedOutputOffer(offer,outputPlan);
        var receipts=state.Receipts.ToDictionary(static x=>x.Key,static x=>x.Value);receipts.Add(offer.OperationId,receipt);var next=new OutputOfferAcceptanceState(receipts);
        return new OutputOfferResult.Accepted(next,receipt,new InMemoryOutputController(outputPlan,maximumOutputReceipts));
    }
    private static bool Axis(ExpectedAuthorityVectorV1 authority,Func<AuthorityAxisValueV1,bool> predicate,out AuthorityAxisValueV1? axis)
    {axis=authority.Axes.Select(static x=>x.Value).SingleOrDefault(predicate);return axis is not null;}
    private static bool SameAuthority(ExpectedAuthorityVectorV1 left,ExpectedAuthorityVectorV1 right)=>left.Session==right.Session&&left.Axes.SequenceEqual(right.Axes);
    private static OutputOfferResult.Rejected Reject(OutputOfferAcceptanceState state,string code)=>new(state,new BoundedAscii(code));
}

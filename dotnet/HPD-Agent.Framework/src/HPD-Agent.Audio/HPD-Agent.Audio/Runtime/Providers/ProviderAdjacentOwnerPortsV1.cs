using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Providers;

internal enum ProviderAdjacentDirectionV1:ushort{SuppliedByProvider=1,ConsumedByProvider=2}
internal enum ProviderAdjacentKindV1:ushort
{
    ParticipantReadiness=1,MediaObservation=2,ActivityEvidence=3,TranscriptEvidence=4,OutputCandidate=5,ToolProposal=6,ProviderHealth=7,
    LifecycleAdmission=8,MediaLease=9,PromotedActivity=10,EndpointDecision=11,PlayoutReceipt=12,ToolDisposition=13,RouteAuthorization=14,
}

internal sealed record ProviderAdjacentMessageV1
{
    internal ProviderAdjacentMessageV1(OperationId operationId,ProviderAdjacentDirectionV1 direction,ProviderAdjacentKindV1 kind,
        OwnerSliceId adjacentOwner,ParticipantId participantId,ProviderGenerationId providerGeneration,
        ExpectedAuthorityVectorV1 authority,JournalPositionV1 sourcePosition,Hash256 evidenceFingerprint)
    {
        if(!operationId.IsValid||!participantId.IsValid||!providerGeneration.IsValid||authority is null||!sourcePosition.IsValid||
            sourcePosition.Session!=authority.Session||evidenceFingerprint==default||!Valid(direction,kind,adjacentOwner))throw new ArgumentException("Adjacent-owner provider message is invalid.");
        var axis=authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Provider>().SingleOrDefault();
        if(axis?.Value!=providerGeneration)throw new ArgumentException("Provider generation is stale.");
        OperationId=operationId;Direction=direction;Kind=kind;AdjacentOwner=adjacentOwner;ParticipantId=participantId;ProviderGeneration=providerGeneration;Authority=authority;SourcePosition=sourcePosition;EvidenceFingerprint=evidenceFingerprint;
    }
    internal OperationId OperationId{get;}internal ProviderAdjacentDirectionV1 Direction{get;}internal ProviderAdjacentKindV1 Kind{get;}internal OwnerSliceId AdjacentOwner{get;}
    internal ParticipantId ParticipantId{get;}internal ProviderGenerationId ProviderGeneration{get;}internal ExpectedAuthorityVectorV1 Authority{get;}internal JournalPositionV1 SourcePosition{get;}internal Hash256 EvidenceFingerprint{get;}
    private static bool Valid(ProviderAdjacentDirectionV1 direction,ProviderAdjacentKindV1 kind,OwnerSliceId owner)=>direction switch
    {
        ProviderAdjacentDirectionV1.SuppliedByProvider=>(kind,owner) is
            (ProviderAdjacentKindV1.ParticipantReadiness,OwnerSliceId.S1) or (ProviderAdjacentKindV1.MediaObservation,OwnerSliceId.S2) or
            (ProviderAdjacentKindV1.ActivityEvidence,OwnerSliceId.S3) or (ProviderAdjacentKindV1.TranscriptEvidence,OwnerSliceId.S4) or
            (ProviderAdjacentKindV1.OutputCandidate,OwnerSliceId.S6) or (ProviderAdjacentKindV1.ToolProposal,OwnerSliceId.S7) or
            (ProviderAdjacentKindV1.ProviderHealth,OwnerSliceId.S8),
        ProviderAdjacentDirectionV1.ConsumedByProvider=>(kind,owner) is
            (ProviderAdjacentKindV1.LifecycleAdmission,OwnerSliceId.S1) or (ProviderAdjacentKindV1.MediaLease,OwnerSliceId.S2) or
            (ProviderAdjacentKindV1.PromotedActivity,OwnerSliceId.S3) or (ProviderAdjacentKindV1.EndpointDecision,OwnerSliceId.S4) or
            (ProviderAdjacentKindV1.PlayoutReceipt,OwnerSliceId.S6) or (ProviderAdjacentKindV1.ToolDisposition,OwnerSliceId.S7) or
            (ProviderAdjacentKindV1.RouteAuthorization,OwnerSliceId.S8),_=>false,
    };
}

internal abstract record ProviderAdjacentPortResultV1
{
    private ProviderAdjacentPortResultV1(){}internal sealed record Accepted(ProviderAdjacentPortStateV1 State,ProviderAdjacentMessageV1 Message):ProviderAdjacentPortResultV1;internal sealed record Duplicate(ProviderAdjacentPortStateV1 State,ProviderAdjacentMessageV1 Message):ProviderAdjacentPortResultV1;internal sealed record Rejected(ProviderAdjacentPortStateV1 State,BoundedAscii SafeCode):ProviderAdjacentPortResultV1;
}
internal sealed class ProviderAdjacentPortStateV1
{
    private readonly ReadOnlyDictionary<OperationId,ProviderAdjacentMessageV1> _messages;
    internal ProviderAdjacentPortStateV1(ProviderParticipantPlanV1 plan,IDictionary<OperationId,ProviderAdjacentMessageV1>? messages=null){Plan=plan??throw new ArgumentNullException(nameof(plan));_messages=new(messages is null?new Dictionary<OperationId,ProviderAdjacentMessageV1>():new Dictionary<OperationId,ProviderAdjacentMessageV1>(messages));}
    internal ProviderParticipantPlanV1 Plan{get;}internal IReadOnlyDictionary<OperationId,ProviderAdjacentMessageV1> Messages=>_messages;
}
internal interface IProviderAdjacentOwnerPortV1{ProviderAdjacentPortResultV1 Exchange(ProviderAdjacentMessageV1 message);}
internal sealed class InMemoryProviderAdjacentOwnerPortV1: IProviderAdjacentOwnerPortV1
{
    private ProviderAdjacentPortStateV1 _state;private readonly ushort _maximumMessages;
    internal InMemoryProviderAdjacentOwnerPortV1(ProviderParticipantPlanV1 plan,ushort maximumMessages){if(maximumMessages==0)throw new ArgumentOutOfRangeException(nameof(maximumMessages));_state=new(plan);_maximumMessages=maximumMessages;}
    public ProviderAdjacentPortResultV1 Exchange(ProviderAdjacentMessageV1 message){var result=ProviderAdjacentOwnerReducerV1.Apply(_state,message,_maximumMessages);if(result is ProviderAdjacentPortResultV1.Accepted accepted)_state=accepted.State;return result;}
}
internal static class ProviderAdjacentOwnerReducerV1
{
    internal static ProviderAdjacentPortResultV1 Apply(ProviderAdjacentPortStateV1 state,ProviderAdjacentMessageV1 message,ushort maximumMessages)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(message);if(maximumMessages==0)throw new ArgumentOutOfRangeException(nameof(maximumMessages));
        if(state.Messages.TryGetValue(message.OperationId,out var prior))return prior==message?new ProviderAdjacentPortResultV1.Duplicate(state,prior):Reject(state,"provider-adjacent-operation-contradiction");
        if(state.Messages.Count>=maximumMessages)return Reject(state,"provider-adjacent-capacity-refused");
        if(message.ParticipantId!=state.Plan.ParticipantId||message.ProviderGeneration!=state.Plan.Generation||!Same(message.Authority,state.Plan.Authority))return Reject(state,"provider-adjacent-authority-mismatch");
        var messages=state.Messages.ToDictionary(static x=>x.Key,static x=>x.Value);messages.Add(message.OperationId,message);var next=new ProviderAdjacentPortStateV1(state.Plan,messages);return new ProviderAdjacentPortResultV1.Accepted(next,message);
    }
    private static bool Same(ExpectedAuthorityVectorV1 x,ExpectedAuthorityVectorV1 y)=>x.Session==y.Session&&x.Axes.SequenceEqual(y.Axes);
    private static ProviderAdjacentPortResultV1.Rejected Reject(ProviderAdjacentPortStateV1 state,string code)=>new(state,new BoundedAscii(code));
}

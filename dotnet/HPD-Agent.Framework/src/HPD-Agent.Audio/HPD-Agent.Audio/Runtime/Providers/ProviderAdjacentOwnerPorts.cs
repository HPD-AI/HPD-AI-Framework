using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Providers;

internal enum ProviderAdjacentDirection:ushort{SuppliedByProvider=1,ConsumedByProvider=2}
internal enum ProviderAdjacentKind:ushort
{
    ParticipantReadiness=1,MediaObservation=2,ActivityEvidence=3,TranscriptEvidence=4,OutputCandidate=5,ToolProposal=6,ProviderHealth=7,
    LifecycleAdmission=8,MediaLease=9,PromotedActivity=10,EndpointDecision=11,PlayoutReceipt=12,ToolDisposition=13,RouteAuthorization=14,
}

internal sealed record ProviderAdjacentMessage
{
    internal ProviderAdjacentMessage(OperationId operationId,ProviderAdjacentDirection direction,ProviderAdjacentKind kind,
        OwnerSliceId adjacentOwner,ParticipantId participantId,ProviderGenerationId providerGeneration,
        ExpectedAuthorityVectorV1 authority,JournalPositionV1 sourcePosition,Hash256 evidenceFingerprint)
    {
        if(!operationId.IsValid||!participantId.IsValid||!providerGeneration.IsValid||authority is null||!sourcePosition.IsValid||
            sourcePosition.Session!=authority.Session||evidenceFingerprint==default||!Valid(direction,kind,adjacentOwner))throw new ArgumentException("Adjacent-owner provider message is invalid.");
        var axis=authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Provider>().SingleOrDefault();
        if(axis?.Value!=providerGeneration)throw new ArgumentException("Provider generation is stale.");
        OperationId=operationId;Direction=direction;Kind=kind;AdjacentOwner=adjacentOwner;ParticipantId=participantId;ProviderGeneration=providerGeneration;Authority=authority;SourcePosition=sourcePosition;EvidenceFingerprint=evidenceFingerprint;
    }
    internal OperationId OperationId{get;}internal ProviderAdjacentDirection Direction{get;}internal ProviderAdjacentKind Kind{get;}internal OwnerSliceId AdjacentOwner{get;}
    internal ParticipantId ParticipantId{get;}internal ProviderGenerationId ProviderGeneration{get;}internal ExpectedAuthorityVectorV1 Authority{get;}internal JournalPositionV1 SourcePosition{get;}internal Hash256 EvidenceFingerprint{get;}
    private static bool Valid(ProviderAdjacentDirection direction,ProviderAdjacentKind kind,OwnerSliceId owner)=>direction switch
    {
        ProviderAdjacentDirection.SuppliedByProvider=>(kind,owner) is
            (ProviderAdjacentKind.ParticipantReadiness,OwnerSliceId.S1) or (ProviderAdjacentKind.MediaObservation,OwnerSliceId.S2) or
            (ProviderAdjacentKind.ActivityEvidence,OwnerSliceId.S3) or (ProviderAdjacentKind.TranscriptEvidence,OwnerSliceId.S4) or
            (ProviderAdjacentKind.OutputCandidate,OwnerSliceId.S6) or (ProviderAdjacentKind.ToolProposal,OwnerSliceId.S7) or
            (ProviderAdjacentKind.ProviderHealth,OwnerSliceId.S8),
        ProviderAdjacentDirection.ConsumedByProvider=>(kind,owner) is
            (ProviderAdjacentKind.LifecycleAdmission,OwnerSliceId.S1) or (ProviderAdjacentKind.MediaLease,OwnerSliceId.S2) or
            (ProviderAdjacentKind.PromotedActivity,OwnerSliceId.S3) or (ProviderAdjacentKind.EndpointDecision,OwnerSliceId.S4) or
            (ProviderAdjacentKind.PlayoutReceipt,OwnerSliceId.S6) or (ProviderAdjacentKind.ToolDisposition,OwnerSliceId.S7) or
            (ProviderAdjacentKind.RouteAuthorization,OwnerSliceId.S8),_=>false,
    };
}

internal abstract record ProviderAdjacentPortResult
{
    private ProviderAdjacentPortResult(){}internal sealed record Accepted(ProviderAdjacentPortState State,ProviderAdjacentMessage Message):ProviderAdjacentPortResult;internal sealed record Duplicate(ProviderAdjacentPortState State,ProviderAdjacentMessage Message):ProviderAdjacentPortResult;internal sealed record Rejected(ProviderAdjacentPortState State,BoundedAscii SafeCode):ProviderAdjacentPortResult;
}
internal sealed class ProviderAdjacentPortState
{
    private readonly ReadOnlyDictionary<OperationId,ProviderAdjacentMessage> _messages;
    internal ProviderAdjacentPortState(ProviderParticipantPlan plan,IDictionary<OperationId,ProviderAdjacentMessage>? messages=null){Plan=plan??throw new ArgumentNullException(nameof(plan));_messages=new(messages is null?new Dictionary<OperationId,ProviderAdjacentMessage>():new Dictionary<OperationId,ProviderAdjacentMessage>(messages));}
    internal ProviderParticipantPlan Plan{get;}internal IReadOnlyDictionary<OperationId,ProviderAdjacentMessage> Messages=>_messages;
}
internal interface IProviderAdjacentOwnerPort{ProviderAdjacentPortResult Exchange(ProviderAdjacentMessage message);}
internal sealed class InMemoryProviderAdjacentOwnerPort: IProviderAdjacentOwnerPort
{
    private ProviderAdjacentPortState _state;private readonly ushort _maximumMessages;
    internal InMemoryProviderAdjacentOwnerPort(ProviderParticipantPlan plan,ushort maximumMessages){if(maximumMessages==0)throw new ArgumentOutOfRangeException(nameof(maximumMessages));_state=new(plan);_maximumMessages=maximumMessages;}
    public ProviderAdjacentPortResult Exchange(ProviderAdjacentMessage message){var result=ProviderAdjacentOwnerReducer.Apply(_state,message,_maximumMessages);if(result is ProviderAdjacentPortResult.Accepted accepted)_state=accepted.State;return result;}
}
internal static class ProviderAdjacentOwnerReducer
{
    internal static ProviderAdjacentPortResult Apply(ProviderAdjacentPortState state,ProviderAdjacentMessage message,ushort maximumMessages)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(message);if(maximumMessages==0)throw new ArgumentOutOfRangeException(nameof(maximumMessages));
        if(state.Messages.TryGetValue(message.OperationId,out var prior))return prior==message?new ProviderAdjacentPortResult.Duplicate(state,prior):Reject(state,"provider-adjacent-operation-contradiction");
        if(state.Messages.Count>=maximumMessages)return Reject(state,"provider-adjacent-capacity-refused");
        if(message.ParticipantId!=state.Plan.ParticipantId||message.ProviderGeneration!=state.Plan.Generation||!Same(message.Authority,state.Plan.Authority))return Reject(state,"provider-adjacent-authority-mismatch");
        var messages=state.Messages.ToDictionary(static x=>x.Key,static x=>x.Value);messages.Add(message.OperationId,message);var next=new ProviderAdjacentPortState(state.Plan,messages);return new ProviderAdjacentPortResult.Accepted(next,message);
    }
    private static bool Same(ExpectedAuthorityVectorV1 x,ExpectedAuthorityVectorV1 y)=>x.Session==y.Session&&x.Axes.SequenceEqual(y.Axes);
    private static ProviderAdjacentPortResult.Rejected Reject(ProviderAdjacentPortState state,string code)=>new(state,new BoundedAscii(code));
}

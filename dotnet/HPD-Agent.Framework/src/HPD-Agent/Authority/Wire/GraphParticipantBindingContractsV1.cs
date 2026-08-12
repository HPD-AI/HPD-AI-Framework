namespace HPD.Agent.Authority;

internal sealed class GraphParticipantReservationCommandV1
{ private readonly byte[] _body; internal GraphParticipantReservationCommandV1(SessionAuthorityStampV1 session,ExpectedAuthorityVectorV1 expectedAuthority,ReadOnlySpan<byte> body){Validate(session,expectedAuthority,body,GraphParticipantBindingCodecsV1.MaximumReservationCommandBodyBytes);Session=session;ExpectedAuthority=expectedAuthority;_body=body.ToArray();Body=Array.AsReadOnly(_body);} internal SessionAuthorityStampV1 Session{get;} internal ExpectedAuthorityVectorV1 ExpectedAuthority{get;} internal IReadOnlyList<byte> Body{get;} internal ReadOnlySpan<byte> BodyBytes=>_body; internal static void Validate(SessionAuthorityStampV1 s,ExpectedAuthorityVectorV1 a,ReadOnlySpan<byte>b,int maximum){if(!s.IsValid||a is null||a.Session!=s||b.Length>maximum)throw new ArgumentException("Invalid graph-participant payload.");} }
internal sealed class GraphParticipantReservationFactV1
{ private readonly byte[] _body; internal GraphParticipantReservationFactV1(SessionAuthorityStampV1 session,ExpectedAuthorityVectorV1 expectedAuthority,ReadOnlySpan<byte> body){GraphParticipantReservationCommandV1.Validate(session,expectedAuthority,body,GraphParticipantBindingCodecsV1.MaximumReservationFactBodyBytes);Session=session;ExpectedAuthority=expectedAuthority;_body=body.ToArray();Body=Array.AsReadOnly(_body);} internal SessionAuthorityStampV1 Session{get;} internal ExpectedAuthorityVectorV1 ExpectedAuthority{get;} internal IReadOnlyList<byte> Body{get;} internal ReadOnlySpan<byte> BodyBytes=>_body; }
internal sealed class GraphParticipantBindingCommandV1
{ private readonly byte[] _body; internal GraphParticipantBindingCommandV1(SessionAuthorityStampV1 session,ExpectedAuthorityVectorV1 expectedAuthority,ReadOnlySpan<byte> body){GraphParticipantReservationCommandV1.Validate(session,expectedAuthority,body,GraphParticipantBindingCodecsV1.MaximumBindingCommandBodyBytes);Session=session;ExpectedAuthority=expectedAuthority;_body=body.ToArray();Body=Array.AsReadOnly(_body);} internal SessionAuthorityStampV1 Session{get;} internal ExpectedAuthorityVectorV1 ExpectedAuthority{get;} internal IReadOnlyList<byte> Body{get;} internal ReadOnlySpan<byte> BodyBytes=>_body; }
internal sealed class GraphParticipantBindingFactV1
{ private readonly byte[] _body; internal GraphParticipantBindingFactV1(SessionAuthorityStampV1 session,ExpectedAuthorityVectorV1 expectedAuthority,ReadOnlySpan<byte> body){GraphParticipantReservationCommandV1.Validate(session,expectedAuthority,body,GraphParticipantBindingCodecsV1.MaximumBindingFactBodyBytes);Session=session;ExpectedAuthority=expectedAuthority;_body=body.ToArray();Body=Array.AsReadOnly(_body);} internal SessionAuthorityStampV1 Session{get;} internal ExpectedAuthorityVectorV1 ExpectedAuthority{get;} internal IReadOnlyList<byte> Body{get;} internal ReadOnlySpan<byte> BodyBytes=>_body; }

internal sealed record GraphParticipantReservationCommandBodyV1
{
    internal GraphParticipantReservationCommandBodyV1(OperationId operationId,JournalPositionV1? expectedReservationFact,RuntimeGenerationId runtimeGeneration,Hash256 participantPlanFingerprint,Hash256 topologyFingerprint,Hash256 executablePlanFingerprint,BoundedAscii participantFactoryKey,IReadOnlyList<BoundedAscii> orderedTopologyNodeKeys,MonotonicStampV1 observedAt)
    {OperationId=operationId;ExpectedReservationFact=expectedReservationFact;RuntimeGeneration=runtimeGeneration;ParticipantPlanFingerprint=participantPlanFingerprint;TopologyFingerprint=topologyFingerprint;ExecutablePlanFingerprint=executablePlanFingerprint;ParticipantFactoryKey=participantFactoryKey;OrderedTopologyNodeKeys=Copy(orderedTopologyNodeKeys);ObservedAt=observedAt;}
    internal OperationId OperationId{get;} internal JournalPositionV1? ExpectedReservationFact{get;} internal RuntimeGenerationId RuntimeGeneration{get;} internal Hash256 ParticipantPlanFingerprint{get;} internal Hash256 TopologyFingerprint{get;} internal Hash256 ExecutablePlanFingerprint{get;} internal BoundedAscii ParticipantFactoryKey{get;} internal IReadOnlyList<BoundedAscii> OrderedTopologyNodeKeys{get;} internal MonotonicStampV1 ObservedAt{get;}
    private static IReadOnlyList<BoundedAscii> Copy(IReadOnlyList<BoundedAscii> source){ArgumentNullException.ThrowIfNull(source);var a=new BoundedAscii[source.Count];for(var i=0;i<a.Length;i++)a[i]=source[i];return Array.AsReadOnly(a);}
}

internal sealed record GraphParticipantReservationFactBodyV1(
    OperationId OperationId, JournalPositionV1 CommandPosition, JournalPositionV1? ActualPredecessor, ushort Outcome,
    RuntimeGenerationId RuntimeGeneration, Hash256 ParticipantPlanFingerprint, Hash256 TopologyFingerprint,
    Hash256 ExecutablePlanFingerprint, GraphParticipantReservationV1? Reservation, BoundedAscii? SafeCode,
    MonotonicStampV1 ObservedAt);

internal sealed record GraphParticipantBindingCommandBodyV1(
    OperationId OperationId, JournalPositionV1 ReservationFact, JournalPositionV1? ExpectedBindingFact,
    GraphGenerationId GraphGeneration, RuntimeGenerationId RuntimeGeneration, Hash256 ParticipantPlanFingerprint,
    Hash256 TopologyFingerprint, Hash256 ExecutablePlanFingerprint, CapacityGrantBindingProofV1 CapacityGrantProof,
    MonotonicStampV1 ObservedAt);

internal sealed record GraphParticipantBindingFactBodyV1(
    OperationId OperationId, JournalPositionV1 CommandPosition, JournalPositionV1 ReservationFact,
    JournalPositionV1? ActualPredecessor, ushort Outcome, GraphGenerationId GraphGeneration,
    RuntimeGenerationId RuntimeGeneration, Hash256 ParticipantPlanFingerprint, Hash256 TopologyFingerprint,
    Hash256 ExecutablePlanFingerprint, GraphParticipantBindingV1? Binding,
    CapacityGrantBindingProofV1? CapacityGrantProof, BoundedAscii? SafeCode, MonotonicStampV1 ObservedAt);

internal sealed record GraphParticipantReservationV1
{ internal GraphParticipantReservationV1(ParticipantId participantId,BoundedAscii participantFactoryKey,IReadOnlyList<BoundedAscii> orderedTopologyNodeKeys){ParticipantId=participantId;ParticipantFactoryKey=participantFactoryKey;OrderedTopologyNodeKeys=Copy(orderedTopologyNodeKeys);} internal ParticipantId ParticipantId{get;} internal BoundedAscii ParticipantFactoryKey{get;} internal IReadOnlyList<BoundedAscii> OrderedTopologyNodeKeys{get;} private static IReadOnlyList<BoundedAscii> Copy(IReadOnlyList<BoundedAscii>s){ArgumentNullException.ThrowIfNull(s);var a=new BoundedAscii[s.Count];for(var i=0;i<a.Length;i++)a[i]=s[i];return Array.AsReadOnly(a);} }

internal sealed record GraphParticipantBindingV1
{ internal GraphParticipantBindingV1(ParticipantId participantId,BoundedAscii participantFactoryKey,IReadOnlyList<BoundedAscii> orderedTopologyNodeKeys){ParticipantId=participantId;ParticipantFactoryKey=participantFactoryKey;OrderedTopologyNodeKeys=Copy(orderedTopologyNodeKeys);} internal ParticipantId ParticipantId{get;} internal BoundedAscii ParticipantFactoryKey{get;} internal IReadOnlyList<BoundedAscii> OrderedTopologyNodeKeys{get;} private static IReadOnlyList<BoundedAscii> Copy(IReadOnlyList<BoundedAscii>s){ArgumentNullException.ThrowIfNull(s);var a=new BoundedAscii[s.Count];for(var i=0;i<a.Length;i++)a[i]=s[i];return Array.AsReadOnly(a);} }

internal sealed record CapacityGrantBindingProofV1(
    CapacityGrantId GrantId, JournalPositionV1 GrantedAt, JournalPositionV1 CurrentFact,
    ushort RequiredChargeCount, Hash256 RequiredChargeCoverageHash);

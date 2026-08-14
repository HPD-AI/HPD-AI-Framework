namespace HPD.Agent.Authority;

internal sealed class GraphParticipantReservationCommandV2
{
    private readonly byte[] _body;
    internal GraphParticipantReservationCommandV2(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    { Validate(session, expectedAuthority, body, GraphParticipantReservationCodecsV2.MaximumReservationCommandBodyBytes); Session=session; ExpectedAuthority=expectedAuthority; _body=body.ToArray(); Body=Array.AsReadOnly(_body); }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> Body { get; }
    internal ReadOnlySpan<byte> BodyBytes => _body;
    internal static void Validate(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body, int maximum)
    { if(!session.IsValid || authority is null || authority.Session!=session || body.Length>maximum) throw new ArgumentException("Invalid graph-participant reservation payload."); }
}

internal sealed class GraphParticipantReservationFactV2
{
    private readonly byte[] _body;
    internal GraphParticipantReservationFactV2(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    { GraphParticipantReservationCommandV2.Validate(session,expectedAuthority,body,GraphParticipantReservationCodecsV2.MaximumReservationFactBodyBytes); Session=session; ExpectedAuthority=expectedAuthority; _body=body.ToArray(); Body=Array.AsReadOnly(_body); }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> Body { get; }
    internal ReadOnlySpan<byte> BodyBytes => _body;
}

internal sealed record GraphParticipantReservationCommandBodyV2
{
    internal GraphParticipantReservationCommandBodyV2(OperationId operationId, JournalPositionV1? expectedReservationFact, RuntimeGenerationId runtimeGeneration, GraphGenerationId graphGeneration, Hash256 participantPlanFingerprint, Hash256 allocationCarrierFingerprint, BoundedAscii participantFactoryKey, IReadOnlyList<BoundedAscii> orderedTopologyNodeKeys, MonotonicStampV1 observedAt)
    { ArgumentNullException.ThrowIfNull(orderedTopologyNodeKeys); if(!operationId.IsValid||(expectedReservationFact is { } predecessor&&!predecessor.IsValid)||!runtimeGeneration.IsValid||!graphGeneration.IsValid||participantPlanFingerprint==default||allocationCarrierFingerprint==default||!participantFactoryKey.IsValid||participantFactoryKey.ToString().Length>128||!observedAt.IsValid)throw new ArgumentException("Invalid graph-participant reservation command body.");if(orderedTopologyNodeKeys.Count is <1 or >64)throw new ArgumentException("Invalid ordered topology node count.");for(var i=0;i<orderedTopologyNodeKeys.Count;i++){if(!orderedTopologyNodeKeys[i].IsValid||orderedTopologyNodeKeys[i].ToString().Length>128)throw new ArgumentException("Invalid ordered topology node key.");if(i>0&&orderedTopologyNodeKeys[i-1].CompareTo(orderedTopologyNodeKeys[i])>=0)throw new ArgumentException("Topology node keys must be strictly ordered and unique.");} OperationId=operationId; ExpectedReservationFact=expectedReservationFact; RuntimeGeneration=runtimeGeneration; GraphGeneration=graphGeneration; ParticipantPlanFingerprint=participantPlanFingerprint; AllocationCarrierFingerprint=allocationCarrierFingerprint; ParticipantFactoryKey=participantFactoryKey; OrderedTopologyNodeKeys=Copy(orderedTopologyNodeKeys); ObservedAt=observedAt; }
    internal OperationId OperationId { get; }
    internal JournalPositionV1? ExpectedReservationFact { get; }
    internal RuntimeGenerationId RuntimeGeneration { get; }
    internal GraphGenerationId GraphGeneration { get; }
    internal Hash256 ParticipantPlanFingerprint { get; }
    internal Hash256 AllocationCarrierFingerprint { get; }
    internal BoundedAscii ParticipantFactoryKey { get; }
    internal IReadOnlyList<BoundedAscii> OrderedTopologyNodeKeys { get; }
    internal MonotonicStampV1 ObservedAt { get; }
    private static IReadOnlyList<BoundedAscii> Copy(IReadOnlyList<BoundedAscii> source) { ArgumentNullException.ThrowIfNull(source); var result=new BoundedAscii[source.Count]; for(var i=0;i<result.Length;i++) result[i]=source[i]; return Array.AsReadOnly(result); }
}

internal sealed record GraphParticipantReservationFactBodyV2
{
    internal GraphParticipantReservationFactBodyV2(OperationId operationId,JournalPositionV1 commandPosition,JournalPositionV1? actualPredecessor,ushort outcome,RuntimeGenerationId runtimeGeneration,GraphGenerationId graphGeneration,Hash256 participantPlanFingerprint,Hash256 allocationCarrierFingerprint,GraphParticipantReservationV1? reservation,BoundedAscii? safeCode,MonotonicStampV1 observedAt)
    {
        if(!operationId.IsValid||!commandPosition.IsValid||(actualPredecessor is { } predecessor&&!predecessor.IsValid)||!runtimeGeneration.IsValid||!graphGeneration.IsValid||participantPlanFingerprint==default||allocationCarrierFingerprint==default||!observedAt.IsValid||outcome is not (1 or 2)||(outcome==1?(reservation is null||safeCode is not null):(reservation is not null||safeCode is null)))throw new ArgumentException("Invalid graph-participant reservation fact body.");
        string[] ReservationSafeCodes=["participant-already-reserved","reservation-predecessor-conflict","authority-stale","plan-fingerprint-mismatch","topology-node-set-mismatch","participant-factory-mismatch","participant-id-collision","invalid-body"];
        if(safeCode is not null&&(!safeCode.Value.IsValid||safeCode.Value.ToString().Length>64||!ReservationSafeCodes.Contains(safeCode.Value.ToString(),StringComparer.Ordinal)))throw new ArgumentException("Invalid reservation safe code.");
        if(reservation is not null){if(!reservation.ParticipantId.IsValid||!reservation.ParticipantFactoryKey.IsValid||reservation.ParticipantFactoryKey.ToString().Length>128||reservation.OrderedTopologyNodeKeys.Count is <1 or >64)throw new ArgumentException("Invalid reservation evidence.");BoundedAscii prior=default;foreach(var node in reservation.OrderedTopologyNodeKeys){if(!node.IsValid||node.ToString().Length>128||(prior.IsValid&&prior.CompareTo(node)>=0))throw new ArgumentException("Invalid reservation node keys.");prior=node;}}
        OperationId=operationId;CommandPosition=commandPosition;ActualPredecessor=actualPredecessor;Outcome=outcome;RuntimeGeneration=runtimeGeneration;GraphGeneration=graphGeneration;ParticipantPlanFingerprint=participantPlanFingerprint;AllocationCarrierFingerprint=allocationCarrierFingerprint;Reservation=reservation;SafeCode=safeCode;ObservedAt=observedAt;
    }
    internal OperationId OperationId { get; }
    internal JournalPositionV1 CommandPosition { get; }
    internal JournalPositionV1? ActualPredecessor { get; }
    internal ushort Outcome { get; }
    internal RuntimeGenerationId RuntimeGeneration { get; }
    internal GraphGenerationId GraphGeneration { get; }
    internal Hash256 ParticipantPlanFingerprint { get; }
    internal Hash256 AllocationCarrierFingerprint { get; }
    internal GraphParticipantReservationV1? Reservation { get; }
    internal BoundedAscii? SafeCode { get; }
    internal MonotonicStampV1 ObservedAt { get; }
}

using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphMediaPhysicalReleaseOutcomeV1 : byte
{
    Released = 1,
    Unknown = 2,
    Rejected = 3,
}

internal sealed record GraphMediaOwnerReleaseProofV1
{
    internal GraphMediaOwnerReleaseProofV1(StableId128 ownerId, OperationId terminalOperationId,
        Hash256 terminalRequestHash, GraphMediaOwnerTransitionResultV1 terminalResult,
        Hash256 ledgerFingerprint, ushort returnedBorrowCount, Hash256 returnedBorrowSetHash)
    {
        if (ownerId.Equals(default) || !terminalOperationId.IsValid || terminalRequestHash == default ||
            terminalResult is not (GraphMediaOwnerTransitionResultV1.Transferred or GraphMediaOwnerTransitionResultV1.Disposed) ||
            ledgerFingerprint == default || returnedBorrowCount > 256 || returnedBorrowSetHash == default)
            throw new ArgumentException("A terminal owner proof is required.");
        OwnerId = ownerId; TerminalOperationId = terminalOperationId; TerminalRequestHash = terminalRequestHash;
        TerminalResult = terminalResult; LedgerFingerprint = ledgerFingerprint;
        ReturnedBorrowCount = returnedBorrowCount; ReturnedBorrowSetHash = returnedBorrowSetHash;
    }
    internal StableId128 OwnerId { get; }
    internal OperationId TerminalOperationId { get; }
    internal Hash256 TerminalRequestHash { get; }
    internal GraphMediaOwnerTransitionResultV1 TerminalResult { get; }
    internal Hash256 LedgerFingerprint { get; }
    internal ushort ReturnedBorrowCount { get; }
    internal Hash256 ReturnedBorrowSetHash { get; }
}

internal sealed record GraphMediaWorkReleaseProofV1
{
    internal GraphMediaWorkReleaseProofV1(Hash256 ledgerFingerprint, GraphMediaReleaseEligibilityV1 eligibility,
        ushort workCount, ushort cleanupCount)
    {
        if (ledgerFingerprint == default || eligibility != GraphMediaReleaseEligibilityV1.Eligible ||
            workCount is 0 or > 64 || cleanupCount is 0 or > 64 || cleanupCount < workCount)
            throw new ArgumentException("An eligible bounded work proof is required.");
        LedgerFingerprint = ledgerFingerprint; Eligibility = eligibility; WorkCount = workCount; CleanupCount = cleanupCount;
    }
    internal Hash256 LedgerFingerprint { get; }
    internal GraphMediaReleaseEligibilityV1 Eligibility { get; }
    internal ushort WorkCount { get; }
    internal ushort CleanupCount { get; }
}

internal sealed record GraphMediaFanoutReleaseProofV1
{
    internal GraphMediaFanoutReleaseProofV1(OperationId operationId, Hash256 requestHash,
        GraphMediaFanoutResultV1 result, Hash256 ledgerFingerprint)
    {
        if (!operationId.IsValid || requestHash == default ||
            result is not (GraphMediaFanoutResultV1.Committed or GraphMediaFanoutResultV1.Reconciled) ||
            ledgerFingerprint == default) throw new ArgumentException("A terminal fanout proof is required.");
        OperationId = operationId; RequestHash = requestHash; Result = result; LedgerFingerprint = ledgerFingerprint;
    }
    internal OperationId OperationId { get; }
    internal Hash256 RequestHash { get; }
    internal GraphMediaFanoutResultV1 Result { get; }
    internal Hash256 LedgerFingerprint { get; }
}

internal sealed record GraphMediaReleaseResidenceProofV1
{
    internal GraphMediaReleaseResidenceProofV1(OperationId operationId, Hash256 requestHash,
        StableId128 residenceId, StableId128 ownerId, GraphGenerationId graphGeneration, BoundedAscii destinationNodeKey,
        ParticipantId participantId, JournalPositionV1 bindingCommandPosition,
        JournalPositionV1 bindingFactPosition, JournalPositionV1 reservationCommandPosition,
        JournalPositionV1 reservationFactPosition, CapacityGrantId grantId, JournalPositionV1 grantedAt,
        JournalPositionV1 currentFact, Hash256 coverageHashV2, Hash256 topologyFingerprint,
        Hash256 executableFingerprint, GraphMediaCapacityAssignmentV1 assignment,
        GraphMediaResidenceClassV1 @class, GraphMediaResidenceStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var session = bindingCommandPosition.Session;
        if (!operationId.IsValid || requestHash == default || residenceId.Equals(default) || ownerId.Equals(default) ||
            !graphGeneration.IsValid || !destinationNodeKey.IsValid || destinationNodeKey.ToString().Length > 128 || !participantId.IsValid ||
            !bindingCommandPosition.IsValid || !bindingFactPosition.IsValid || !reservationCommandPosition.IsValid ||
            !reservationFactPosition.IsValid || !grantId.IsValid || !grantedAt.IsValid || !currentFact.IsValid ||
            new[] { bindingFactPosition, reservationCommandPosition, reservationFactPosition, grantedAt, currentFact }
                .Any(position => position.Session != session) ||
            coverageHashV2 == default || topologyFingerprint == default || executableFingerprint == default ||
            @class != GraphMediaResidenceClassV1.Controlled || state != GraphMediaResidenceStateV1.Visible)
            throw new ArgumentException("A visible controlled residence proof is required.");
        OperationId = operationId; RequestHash = requestHash; ResidenceId = residenceId; OwnerId = ownerId; GraphGeneration = graphGeneration;
        DestinationNodeKey = destinationNodeKey; ParticipantId = participantId;
        BindingCommandPosition = bindingCommandPosition; BindingFactPosition = bindingFactPosition;
        ReservationCommandPosition = reservationCommandPosition; ReservationFactPosition = reservationFactPosition;
        GrantId = grantId; GrantedAt = grantedAt; CurrentFact = currentFact; CoverageHashV2 = coverageHashV2;
        TopologyFingerprint = topologyFingerprint; ExecutableFingerprint = executableFingerprint;
        Assignment = assignment; Class = @class; State = state;
    }
    internal OperationId OperationId { get; }
    internal Hash256 RequestHash { get; }
    internal StableId128 ResidenceId { get; }
    internal StableId128 OwnerId { get; }
    internal GraphGenerationId GraphGeneration { get; }
    internal BoundedAscii DestinationNodeKey { get; }
    internal ParticipantId ParticipantId { get; }
    internal JournalPositionV1 BindingCommandPosition { get; }
    internal JournalPositionV1 BindingFactPosition { get; }
    internal JournalPositionV1 ReservationCommandPosition { get; }
    internal JournalPositionV1 ReservationFactPosition { get; }
    internal CapacityGrantId GrantId { get; }
    internal JournalPositionV1 GrantedAt { get; }
    internal JournalPositionV1 CurrentFact { get; }
    internal Hash256 CoverageHashV2 { get; }
    internal Hash256 TopologyFingerprint { get; }
    internal Hash256 ExecutableFingerprint { get; }
    internal GraphMediaCapacityAssignmentV1 Assignment { get; }
    internal GraphMediaResidenceClassV1 Class { get; }
    internal GraphMediaResidenceStateV1 State { get; }
    internal static GraphMediaReleaseResidenceProofV1 FromResidence(GraphMediaControlledResidenceV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value.OperationId, value.RequestHash, value.ResidenceId, value.OwnerId, value.OwnerKey.GraphGeneration, value.DestinationNodeKey,
            value.ParticipantId, value.BindingCommandPosition, value.BindingFactPosition,
            value.ReservationCommandPosition, value.ReservationFactPosition, value.GrantId, value.GrantedAt,
            value.CurrentFact, value.CoverageHashV2, value.TopologyFingerprint, value.ExecutableFingerprint,
            value.Assignment, value.Class, value.State);
    }
}

internal sealed record GraphMediaPhysicalReleaseCommandBodyV1
{
    internal GraphMediaPhysicalReleaseCommandBodyV1(OperationId operationId,
        GraphMediaReleaseResidenceProofV1 residence, GraphMediaOwnerReleaseProofV1 ownerProof,
        GraphMediaWorkReleaseProofV1 workProof, GraphMediaFanoutReleaseProofV1? fanoutProof,
        JournalPositionV1? expectedReleaseFact, MonotonicStampV1 observedAt)
    {
        if (!operationId.IsValid || residence is null || ownerProof is null || workProof is null ||
            !observedAt.IsValid || !residence.OwnerId.Equals(ownerProof.OwnerId) ||
            expectedReleaseFact is { } predecessor && predecessor.Session != residence.BindingCommandPosition.Session)
            throw new ArgumentException("A valid physical-release command body is required.");
        OperationId = operationId; Residence = residence; OwnerProof = ownerProof; WorkProof = workProof;
        FanoutProof = fanoutProof; ExpectedReleaseFact = expectedReleaseFact; ObservedAt = observedAt;
    }
    internal OperationId OperationId { get; }
    internal GraphMediaReleaseResidenceProofV1 Residence { get; }
    internal GraphMediaOwnerReleaseProofV1 OwnerProof { get; }
    internal GraphMediaWorkReleaseProofV1 WorkProof { get; }
    internal GraphMediaFanoutReleaseProofV1? FanoutProof { get; }
    internal JournalPositionV1? ExpectedReleaseFact { get; }
    internal MonotonicStampV1 ObservedAt { get; }
}

internal sealed record GraphMediaPhysicalReleaseFactBodyV1
{
    private static readonly HashSet<string> RejectionCodes = new(StringComparer.Ordinal)
    { "release-authority-stale", "owner-terminal-mismatch", "work-encumbered", "fanout-incomplete",
      "residence-mismatch", "capacity-proof-mismatch", "release-predecessor-conflict" };
    internal GraphMediaPhysicalReleaseFactBodyV1(JournalPositionV1 commandPosition, StableId128 residenceId,
        Hash256 residenceRequestHash, CapacityGrantId grantId, JournalPositionV1 currentFact,
        GraphMediaCapacityAssignmentV1 assignment, GraphMediaPhysicalReleaseOutcomeV1 outcome,
        Hash256? evidenceHash, BoundedAscii? safeCode, MonotonicStampV1 observedAt)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var code = safeCode?.ToString();
        if (!commandPosition.IsValid || residenceId.Equals(default) || residenceRequestHash == default ||
            !grantId.IsValid || !currentFact.IsValid || currentFact.Session != commandPosition.Session ||
            !Enum.IsDefined(outcome) || !observedAt.IsValid ||
            outcome == GraphMediaPhysicalReleaseOutcomeV1.Released &&
                (evidenceHash is null || evidenceHash.Value == default || safeCode is not null) ||
            outcome == GraphMediaPhysicalReleaseOutcomeV1.Unknown && (evidenceHash is not null || safeCode is not null) ||
            outcome == GraphMediaPhysicalReleaseOutcomeV1.Rejected &&
                (evidenceHash is not null || safeCode is null || code!.Length > 64 || !RejectionCodes.Contains(code)))
            throw new ArgumentException("The physical-release outcome arms are invalid.");
        CommandPosition = commandPosition; ResidenceId = residenceId; ResidenceRequestHash = residenceRequestHash;
        GrantId = grantId; CurrentFact = currentFact; Assignment = assignment; Outcome = outcome;
        EvidenceHash = evidenceHash; SafeCode = safeCode; ObservedAt = observedAt;
    }
    internal JournalPositionV1 CommandPosition { get; }
    internal StableId128 ResidenceId { get; }
    internal Hash256 ResidenceRequestHash { get; }
    internal CapacityGrantId GrantId { get; }
    internal JournalPositionV1 CurrentFact { get; }
    internal GraphMediaCapacityAssignmentV1 Assignment { get; }
    internal GraphMediaPhysicalReleaseOutcomeV1 Outcome { get; }
    internal Hash256? EvidenceHash { get; }
    internal BoundedAscii? SafeCode { get; }
    internal MonotonicStampV1 ObservedAt { get; }
}

internal sealed class GraphMediaPhysicalReleaseOuterV1
{
    private readonly byte[] _body;
    internal GraphMediaPhysicalReleaseOuterV1(SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(expectedAuthority);
        if (!session.IsValid || expectedAuthority.Session != session || body.Length > GraphMediaPhysicalReleaseCodecsV1.MaximumBodyBytes)
            throw new ArgumentException("A valid release outer is required.");
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray(); BodyBytes = Array.AsReadOnly(_body);
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> BodyBytes { get; }
    internal ReadOnlyMemory<byte> BodyMemory => _body;
}

internal static class GraphMediaPhysicalReleaseCodecsV1
{
    internal const ushort Major = 1, Minor = 0;
    internal const int MaximumBodyBytes = 32_768, MaximumOuterBytes = 49_152;
    internal const string CommandSchemaId = "hpd.authority-payload-graph-media-physical-release-command.v1";
    internal const string FactSchemaId = "hpd.authority-payload-graph-media-physical-release-fact.v1";
    internal const string CommandBodySchemaId = "hpd.graph-media-physical-release-command-body.v1";
    internal const string FactBodySchemaId = "hpd.graph-media-physical-release-fact-body.v1";
    internal const string OwnerProofSchemaId = "hpd.graph-media-owner-release-proof.v1";
    internal const string WorkProofSchemaId = "hpd.graph-media-work-release-proof.v1";
    internal const string FanoutProofSchemaId = "hpd.graph-media-fanout-release-proof.v1";
    internal const string ResidenceProofSchemaId = "hpd.graph-media-release-residence-proof.v1";

    internal static byte[] EncodeOuter(GraphMediaPhysicalReleaseOuterV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); var writer = Map(3);
        Tag(writer, 1); SessionAuthorityStampV1Codec.Write(writer, value.Session);
        Tag(writer, 2); AuthorityVectorCodecsV1.WriteVector(writer, value.ExpectedAuthority);
        Tag(writer, 3); writer.WriteByteString(value.BodyMemory.Span); return Finish(writer);
    }

    internal static bool TryDecodeOuter(ReadOnlyMemory<byte> bytes, out GraphMediaPhysicalReleaseOuterV1? value)
    {
        value = null; if (bytes.Length > MaximumOuterBytes) return false;
        try
        {
            var reader = new CborReader(bytes, CborConformanceMode.Ctap2Canonical, false); Start(reader, 3);
            Need(reader, 1); var session = SessionAuthorityStampV1Codec.Read(reader);
            Need(reader, 2); var authority = AuthorityVectorCodecsV1.ReadVector(reader);
            Need(reader, 3); var body = reader.ReadByteString(); reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || body.Length > MaximumBodyBytes) return false;
            var candidate = new GraphMediaPhysicalReleaseOuterV1(session, authority, body);
            if (!bytes.Span.SequenceEqual(EncodeOuter(candidate))) return false; value = candidate; return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
    }

    internal static byte[] EncodeCommandBody(GraphMediaPhysicalReleaseCommandBodyV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); var writer = Map(7);
        Tag(writer, 1); WriteOperation(writer, value.OperationId);
        Tag(writer, 2); WriteResidence(writer, value.Residence);
        Tag(writer, 3); WriteOwnerProof(writer, value.OwnerProof);
        Tag(writer, 4); WriteWorkProof(writer, value.WorkProof);
        Tag(writer, 5); WriteOptional(writer, value.FanoutProof, WriteFanoutProof);
        Tag(writer, 6); WritePositionOptional(writer, value.ExpectedReleaseFact);
        Tag(writer, 7); writer.WriteEncodedValue(MonotonicStampV1Codec.Encode(value.ObservedAt)); return Finish(writer);
    }

    internal static bool TryDecodeCommandBody(ReadOnlyMemory<byte> bytes, out GraphMediaPhysicalReleaseCommandBodyV1? value) =>
        TryBody(bytes, ReadCommandBody, EncodeCommandBody, out value);

    internal static byte[] EncodeFactBody(GraphMediaPhysicalReleaseFactBodyV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); var writer = Map(10);
        Tag(writer, 1); AuthorityPositionCodecsV1.Write(writer, value.CommandPosition);
        Tag(writer, 2); WriteStable(writer, value.ResidenceId);
        Tag(writer, 3); WriteHash(writer, value.ResidenceRequestHash);
        Tag(writer, 4); WriteGrant(writer, value.GrantId);
        Tag(writer, 5); AuthorityPositionCodecsV1.Write(writer, value.CurrentFact);
        Tag(writer, 6); WriteAssignment(writer, value.Assignment);
        Tag(writer, 7); writer.WriteUInt64((byte)value.Outcome);
        Tag(writer, 8); WriteHashOptional(writer, value.EvidenceHash);
        Tag(writer, 9); WriteAsciiOptional(writer, value.SafeCode);
        Tag(writer, 10); writer.WriteEncodedValue(MonotonicStampV1Codec.Encode(value.ObservedAt)); return Finish(writer);
    }

    internal static bool TryDecodeFactBody(ReadOnlyMemory<byte> bytes, out GraphMediaPhysicalReleaseFactBodyV1? value) =>
        TryBody(bytes, ReadFactBody, EncodeFactBody, out value);

    internal static byte[] Encode(GraphMediaOwnerReleaseProofV1 value){var writer=new CborWriter(CborConformanceMode.Ctap2Canonical);WriteOwnerProof(writer,value);return writer.Encode();}
    internal static byte[] Encode(GraphMediaWorkReleaseProofV1 value){var writer=new CborWriter(CborConformanceMode.Ctap2Canonical);WriteWorkProof(writer,value);return writer.Encode();}
    internal static byte[] Encode(GraphMediaFanoutReleaseProofV1 value){var writer=new CborWriter(CborConformanceMode.Ctap2Canonical);WriteFanoutProof(writer,value);return writer.Encode();}
    internal static byte[] Encode(GraphMediaReleaseResidenceProofV1 value){var writer=new CborWriter(CborConformanceMode.Ctap2Canonical);WriteResidence(writer,value);return writer.Encode();}
    internal static Hash256 ComputeHash(GraphMediaPhysicalReleaseCommandBodyV1 value)=>AuthorityIntegrityHashV1.Compute(CommandBodySchemaId,Major,Minor,EncodeCommandBody(value));
    internal static Hash256 ComputeHash(GraphMediaPhysicalReleaseFactBodyV1 value)=>AuthorityIntegrityHashV1.Compute(FactBodySchemaId,Major,Minor,EncodeFactBody(value));
    internal static Hash256 ComputeHash(GraphMediaOwnerReleaseProofV1 value)=>AuthorityIntegrityHashV1.Compute(OwnerProofSchemaId,Major,Minor,Encode(value));
    internal static Hash256 ComputeHash(GraphMediaWorkReleaseProofV1 value)=>AuthorityIntegrityHashV1.Compute(WorkProofSchemaId,Major,Minor,Encode(value));
    internal static Hash256 ComputeHash(GraphMediaFanoutReleaseProofV1 value)=>AuthorityIntegrityHashV1.Compute(FanoutProofSchemaId,Major,Minor,Encode(value));
    internal static Hash256 ComputeHash(GraphMediaReleaseResidenceProofV1 value)=>AuthorityIntegrityHashV1.Compute(ResidenceProofSchemaId,Major,Minor,Encode(value));

    private static GraphMediaPhysicalReleaseCommandBodyV1 ReadCommandBody(CborReader reader)
    {
        Start(reader, 7); Need(reader, 1); var operation = ReadOperation(reader);
        Need(reader, 2); var residence = ReadResidence(reader); Need(reader, 3); var owner = ReadOwnerProof(reader);
        Need(reader, 4); var work = ReadWorkProof(reader); Need(reader, 5); var fanout = ReadOptional(reader, ReadFanoutProof);
        Need(reader, 6); var predecessor = ReadPositionOptional(reader); Need(reader, 7); var observed = ReadStamp(reader);
        reader.ReadEndMap(); return new(operation, residence, owner, work, fanout, predecessor, observed);
    }

    private static GraphMediaPhysicalReleaseFactBodyV1 ReadFactBody(CborReader reader)
    {
        Start(reader, 10); Need(reader, 1); var command = AuthorityPositionCodecsV1.ReadJournal(reader);
        Need(reader, 2); var residence = ReadStable(reader); Need(reader, 3); var request = ReadHash(reader);
        Need(reader, 4); var grant = ReadGrant(reader); Need(reader, 5); var current = AuthorityPositionCodecsV1.ReadJournal(reader);
        Need(reader, 6); var assignment = ReadAssignment(reader); Need(reader, 7); var outcome = checked((GraphMediaPhysicalReleaseOutcomeV1)reader.ReadUInt64());
        Need(reader, 8); var evidence = ReadHashOptional(reader); Need(reader, 9); var code = ReadAsciiOptional(reader);
        Need(reader, 10); var observed = ReadStamp(reader); reader.ReadEndMap();
        return new(command, residence, request, grant, current, assignment, outcome, evidence, code, observed);
    }

    private static void WriteResidence(CborWriter writer, GraphMediaReleaseResidenceProofV1 value)
    {
        writer.WriteStartMap(20); Tag(writer, 1); WriteOperation(writer, value.OperationId); Tag(writer, 2); WriteHash(writer, value.RequestHash);
        Tag(writer, 3); WriteStable(writer, value.ResidenceId); Tag(writer, 4); WriteStable(writer, value.OwnerId);
        Tag(writer, 5); WriteGraph(writer, value.GraphGeneration); Tag(writer, 6); WriteAscii(writer, value.DestinationNodeKey);
        Tag(writer, 7); WriteParticipant(writer, value.ParticipantId); Tag(writer, 8); AuthorityPositionCodecsV1.Write(writer, value.BindingCommandPosition);
        Tag(writer, 9); AuthorityPositionCodecsV1.Write(writer, value.BindingFactPosition); Tag(writer, 10); AuthorityPositionCodecsV1.Write(writer, value.ReservationCommandPosition);
        Tag(writer, 11); AuthorityPositionCodecsV1.Write(writer, value.ReservationFactPosition); Tag(writer, 12); WriteGrant(writer, value.GrantId);
        Tag(writer, 13); AuthorityPositionCodecsV1.Write(writer, value.GrantedAt); Tag(writer, 14); AuthorityPositionCodecsV1.Write(writer, value.CurrentFact);
        Tag(writer, 15); WriteHash(writer, value.CoverageHashV2); Tag(writer, 16); WriteHash(writer, value.TopologyFingerprint);
        Tag(writer, 17); WriteHash(writer, value.ExecutableFingerprint); Tag(writer, 18); WriteAssignment(writer, value.Assignment);
        Tag(writer, 19); writer.WriteUInt64((byte)value.Class); Tag(writer, 20); writer.WriteUInt64((byte)value.State); writer.WriteEndMap();
    }

    private static GraphMediaReleaseResidenceProofV1 ReadResidence(CborReader reader)
    {
        Start(reader, 20); Need(reader, 1); var operation = ReadOperation(reader); Need(reader, 2); var request = ReadHash(reader);
        Need(reader, 3); var residence = ReadStable(reader); Need(reader, 4); var owner = ReadStable(reader); Need(reader, 5); var graph = ReadGraph(reader);
        Need(reader, 6); var node = ReadAscii(reader); Need(reader, 7); var participant = ReadParticipant(reader);
        Need(reader, 8); var bindingCommand = AuthorityPositionCodecsV1.ReadJournal(reader); Need(reader, 9); var bindingFact = AuthorityPositionCodecsV1.ReadJournal(reader);
        Need(reader, 10); var reservationCommand = AuthorityPositionCodecsV1.ReadJournal(reader); Need(reader, 11); var reservationFact = AuthorityPositionCodecsV1.ReadJournal(reader);
        Need(reader, 12); var grant = ReadGrant(reader); Need(reader, 13); var granted = AuthorityPositionCodecsV1.ReadJournal(reader);
        Need(reader, 14); var current = AuthorityPositionCodecsV1.ReadJournal(reader); Need(reader, 15); var coverage = ReadHash(reader);
        Need(reader, 16); var topology = ReadHash(reader); Need(reader, 17); var executable = ReadHash(reader); Need(reader, 18); var assignment = ReadAssignment(reader);
        Need(reader, 19); var @class = checked((GraphMediaResidenceClassV1)reader.ReadUInt64()); Need(reader, 20); var state = checked((GraphMediaResidenceStateV1)reader.ReadUInt64()); reader.ReadEndMap();
        return new(operation, request, residence, owner, graph, node, participant, bindingCommand, bindingFact,
            reservationCommand, reservationFact, grant, granted, current, coverage, topology, executable, assignment, @class, state);
    }

    private static void WriteOwnerProof(CborWriter writer, GraphMediaOwnerReleaseProofV1 value)
    {
        writer.WriteStartMap(7); Tag(writer, 1); WriteStable(writer, value.OwnerId); Tag(writer, 2); WriteOperation(writer, value.TerminalOperationId);
        Tag(writer, 3); WriteHash(writer, value.TerminalRequestHash); Tag(writer, 4); writer.WriteUInt64((byte)value.TerminalResult);
        Tag(writer, 5); WriteHash(writer, value.LedgerFingerprint); Tag(writer, 6); writer.WriteUInt64(value.ReturnedBorrowCount);
        Tag(writer, 7); WriteHash(writer, value.ReturnedBorrowSetHash); writer.WriteEndMap();
    }
    private static GraphMediaOwnerReleaseProofV1 ReadOwnerProof(CborReader reader)
    {
        Start(reader, 7); Need(reader, 1); var owner = ReadStable(reader); Need(reader, 2); var operation = ReadOperation(reader);
        Need(reader, 3); var request = ReadHash(reader); Need(reader, 4); var result = checked((GraphMediaOwnerTransitionResultV1)reader.ReadUInt64());
        Need(reader, 5); var ledger = ReadHash(reader); Need(reader, 6); var count = checked((ushort)reader.ReadUInt64());
        Need(reader, 7); var set = ReadHash(reader); reader.ReadEndMap(); return new(owner, operation, request, result, ledger, count, set);
    }
    private static void WriteWorkProof(CborWriter writer, GraphMediaWorkReleaseProofV1 value)
    { writer.WriteStartMap(4); Tag(writer, 1); WriteHash(writer, value.LedgerFingerprint); Tag(writer, 2); writer.WriteUInt64((byte)value.Eligibility); Tag(writer, 3); writer.WriteUInt64(value.WorkCount); Tag(writer, 4); writer.WriteUInt64(value.CleanupCount); writer.WriteEndMap(); }
    private static GraphMediaWorkReleaseProofV1 ReadWorkProof(CborReader reader)
    { Start(reader, 4); Need(reader, 1); var ledger = ReadHash(reader); Need(reader, 2); var eligibility = checked((GraphMediaReleaseEligibilityV1)reader.ReadUInt64()); Need(reader, 3); var work = checked((ushort)reader.ReadUInt64()); Need(reader, 4); var cleanup = checked((ushort)reader.ReadUInt64()); reader.ReadEndMap(); return new(ledger, eligibility, work, cleanup); }
    private static void WriteFanoutProof(CborWriter writer, GraphMediaFanoutReleaseProofV1 value)
    { writer.WriteStartMap(4); Tag(writer, 1); WriteOperation(writer, value.OperationId); Tag(writer, 2); WriteHash(writer, value.RequestHash); Tag(writer, 3); writer.WriteUInt64((byte)value.Result); Tag(writer, 4); WriteHash(writer, value.LedgerFingerprint); writer.WriteEndMap(); }
    private static GraphMediaFanoutReleaseProofV1 ReadFanoutProof(CborReader reader)
    { Start(reader, 4); Need(reader, 1); var operation = ReadOperation(reader); Need(reader, 2); var request = ReadHash(reader); Need(reader, 3); var result = checked((GraphMediaFanoutResultV1)reader.ReadUInt64()); Need(reader, 4); var ledger = ReadHash(reader); reader.ReadEndMap(); return new(operation, request, result, ledger); }

    private static void WriteAssignment(CborWriter writer, GraphMediaCapacityAssignmentV1 value)
    { writer.WriteStartMap(2); Tag(writer, 1); WriteCharge(writer, value.Charge); Tag(writer, 2); writer.WriteUInt64((byte)value.Arm); writer.WriteEndMap(); }
    private static GraphMediaCapacityAssignmentV1 ReadAssignment(CborReader reader)
    { Start(reader, 2); Need(reader, 1); var charge = ReadCharge(reader); Need(reader, 2); var arm = checked((GraphMediaRepresentationArmV1)reader.ReadUInt64()); reader.ReadEndMap(); if (!Enum.IsDefined(arm)) throw Bad(); return new(charge, arm); }
    private static void WriteCharge(CborWriter writer, CapacityChargeV1 value)
    {
        writer.WriteStartMap(5); Tag(writer, 1); writer.WriteUInt64(value.DimensionId.Value); Tag(writer, 2); writer.WriteEncodedValue(CapacityScopeCanonicalCodecV1.Encode(value.Scope));
        Tag(writer, 3); writer.WriteInt64(value.Amount); Tag(writer, 4); WritePurpose(writer, value.Purpose); Tag(writer, 5); WriteWindow(writer, value.Window); writer.WriteEndMap();
    }
    private static CapacityChargeV1 ReadCharge(CborReader reader)
    {
        Start(reader, 5); Need(reader, 1); var dimension = new CapacityDimensionId(checked((ushort)reader.ReadUInt64())); Need(reader, 2);
        if (!CapacityScopeCanonicalCodecV1.TryDecode(reader.ReadEncodedValue(), out var scope)) throw Bad(); Need(reader, 3); var amount = reader.ReadInt64();
        Need(reader, 4); var purpose = CapacityPurposeId.FromValue(ReadStable(reader)); Need(reader, 5); var window = ReadWindow(reader); reader.ReadEndMap();
        return new(dimension, scope!, amount, purpose, window);
    }
    private static void WriteWindow(CborWriter writer, CapacityChargeWindowV1 value)
    { writer.WriteStartMap(value is CapacityChargeWindowV1.EndsAt ? 2 : 1); Tag(writer, 1); writer.WriteUInt64((ushort)value.Kind); if (value is CapacityChargeWindowV1.EndsAt at) { Tag(writer, 2); writer.WriteEncodedValue(MonotonicStampV1Codec.Encode(at.Value)); } writer.WriteEndMap(); }
    private static CapacityChargeWindowV1 ReadWindow(CborReader reader)
    { var count = reader.ReadStartMap(); Need(reader, 1); var kind = checked((CapacityChargeWindowKindV1)reader.ReadUInt64()); CapacityChargeWindowV1 result = kind switch { CapacityChargeWindowKindV1.NoWindow when count == 1 => new CapacityChargeWindowV1.NoWindow(), CapacityChargeWindowKindV1.EndsAt when count == 2 => ReadEndsAt(reader), _ => throw Bad() }; reader.ReadEndMap(); return result; }
    private static CapacityChargeWindowV1 ReadEndsAt(CborReader reader) { Need(reader, 2); return new CapacityChargeWindowV1.EndsAt(ReadStamp(reader)); }

    private static bool TryBody<T>(ReadOnlyMemory<byte> bytes, Func<CborReader, T> read, Func<T, byte[]> write, out T? value) where T : class
    { value = null; if (bytes.Length > MaximumBodyBytes) return false; try { var reader = new CborReader(bytes, CborConformanceMode.Ctap2Canonical, false); var candidate = read(reader); if (reader.BytesRemaining != 0 || !bytes.Span.SequenceEqual(write(candidate))) return false; value = candidate; return true; } catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; } }
    private static CborWriter Map(int count) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(count); return writer; }
    private static byte[] Finish(CborWriter writer) { writer.WriteEndMap(); return writer.Encode(); }
    private static void Start(CborReader reader, int count) { if (reader.ReadStartMap() != count) throw Bad(); }
    private static void Tag(CborWriter writer, ulong value) => writer.WriteUInt64(value);
    private static void Need(CborReader reader, ulong value) { if (reader.ReadUInt64() != value) throw Bad(); }
    private static ArgumentException Bad() => new("Unexpected physical-release wire shape.");
    private static MonotonicStampV1 ReadStamp(CborReader reader) { if (!MonotonicStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var value)) throw Bad(); return value; }
    private static void WriteOptional<T>(CborWriter writer, T? value, Action<CborWriter, T> write) where T : class
    { writer.WriteStartMap(value is null ? 1 : 2); Tag(writer, 1); writer.WriteUInt64(value is null ? 0UL : 1UL); if (value is not null) { Tag(writer, 2); write(writer, value); } writer.WriteEndMap(); }
    private static T? ReadOptional<T>(CborReader reader, Func<CborReader, T> read) where T : class
    { var count = reader.ReadStartMap(); Need(reader, 1); var arm = reader.ReadUInt64(); if (count == 1 && arm == 0) { reader.ReadEndMap(); return null; } if (count != 2 || arm != 1) throw Bad(); Need(reader, 2); var value = read(reader); reader.ReadEndMap(); return value; }
    private static void WritePositionOptional(CborWriter writer, JournalPositionV1? value)
    { writer.WriteStartMap(value is null ? 1 : 2); Tag(writer, 1); writer.WriteUInt64(value is null ? 0UL : 1UL); if (value is { } position) { Tag(writer, 2); AuthorityPositionCodecsV1.Write(writer, position); } writer.WriteEndMap(); }
    private static JournalPositionV1? ReadPositionOptional(CborReader reader)
    { var count = reader.ReadStartMap(); Need(reader, 1); var arm = reader.ReadUInt64(); if (count == 1 && arm == 0) { reader.ReadEndMap(); return null; } if (count != 2 || arm != 1) throw Bad(); Need(reader, 2); var value = AuthorityPositionCodecsV1.ReadJournal(reader); reader.ReadEndMap(); return value; }
    private static void WriteHashOptional(CborWriter writer, Hash256? value)
    { writer.WriteStartMap(value is null ? 1 : 2); Tag(writer, 1); writer.WriteUInt64(value is null ? 0UL : 1UL); if (value is { } hash) { Tag(writer, 2); WriteHash(writer, hash); } writer.WriteEndMap(); }
    private static Hash256? ReadHashOptional(CborReader reader)
    { var count = reader.ReadStartMap(); Need(reader, 1); var arm = reader.ReadUInt64(); if (count == 1 && arm == 0) { reader.ReadEndMap(); return null; } if (count != 2 || arm != 1) throw Bad(); Need(reader, 2); var value = ReadHash(reader); reader.ReadEndMap(); return value; }
    private static void WriteAsciiOptional(CborWriter writer, BoundedAscii? value)
    { writer.WriteStartMap(value is null ? 1 : 2); Tag(writer, 1); writer.WriteUInt64(value is null ? 0UL : 1UL); if (value is { } ascii) { Tag(writer, 2); WriteAscii(writer, ascii); } writer.WriteEndMap(); }
    private static BoundedAscii? ReadAsciiOptional(CborReader reader)
    { var count = reader.ReadStartMap(); Need(reader, 1); var arm = reader.ReadUInt64(); if (count == 1 && arm == 0) { reader.ReadEndMap(); return null; } if (count != 2 || arm != 1) throw Bad(); Need(reader, 2); var value = ReadAscii(reader); reader.ReadEndMap(); return value; }
    private static void WriteAscii(CborWriter writer, BoundedAscii value) { if (!value.IsValid || value.ToString().Length > 128) throw Bad(); BoundedAsciiCodec.Write(writer, value); }
    private static BoundedAscii ReadAscii(CborReader reader) { var value = BoundedAsciiCodec.Read(reader); if (value.ToString().Length > 128) throw Bad(); return value; }
    private static void WriteHash(CborWriter writer, Hash256 value) { Span<byte> bytes = stackalloc byte[32]; if (!value.TryWriteBytes(bytes)) throw Bad(); writer.WriteByteString(bytes); }
    private static Hash256 ReadHash(CborReader reader) { Span<byte> bytes = stackalloc byte[32]; if (!reader.TryReadByteString(bytes, out var written) || written != 32) throw Bad(); return Hash256.FromBytes(bytes); }
    private static StableId128 ReadStable(CborReader reader) { Span<byte> bytes = stackalloc byte[16]; if (!reader.TryReadByteString(bytes, out var written) || written != 16) throw Bad(); return StableId128.FromBytes(bytes); }
    private static void WriteStable(CborWriter writer, StableId128 value) { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw Bad(); writer.WriteByteString(bytes); }
    private static void WriteOperation(CborWriter writer, OperationId value) { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw Bad(); writer.WriteByteString(bytes); }
    private static OperationId ReadOperation(CborReader reader) => OperationId.FromValue(ReadStable(reader));
    private static void WriteParticipant(CborWriter writer, ParticipantId value) { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw Bad(); writer.WriteByteString(bytes); }
    private static ParticipantId ReadParticipant(CborReader reader) => ParticipantId.FromValue(ReadStable(reader));
    private static void WriteGraph(CborWriter writer, GraphGenerationId value) { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw Bad(); writer.WriteByteString(bytes); }
    private static GraphGenerationId ReadGraph(CborReader reader) => GraphGenerationId.FromValue(ReadStable(reader));
    private static void WriteGrant(CborWriter writer, CapacityGrantId value) { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw Bad(); writer.WriteByteString(bytes); }
    private static CapacityGrantId ReadGrant(CborReader reader) => CapacityGrantId.FromValue(ReadStable(reader));
    private static void WritePurpose(CborWriter writer, CapacityPurposeId value) { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw Bad(); writer.WriteByteString(bytes); }
}

internal static class GraphMediaPhysicalReleaseFactIdsV1
{
    private static ReadOnlySpan<byte> CommandDomain => "hpd-graph-media-physical-release-command-fact-id-v1\0"u8;
    private static ReadOnlySpan<byte> FactDomain => "hpd-graph-media-physical-release-result-fact-id-v1\0"u8;
    internal static JournalFactId Command(SessionAuthorityStampV1 session, OperationId operationId) =>
        Derive(CommandDomain, session, operationId, null);
    internal static JournalFactId Fact(JournalPositionV1 commandPosition) =>
        Derive(FactDomain, commandPosition.Session, default, commandPosition);
    private static JournalFactId Derive(ReadOnlySpan<byte> domain, SessionAuthorityStampV1 session,
        OperationId operation, JournalPositionV1? position)
    {
        if (!session.IsValid || position is null && !operation.IsValid || position is { IsValid: false })
            throw new ArgumentException("Valid release identity is required.");
        var secondLength = position is null ? 16 : 8;
        var preimage = new byte[domain.Length + 1 + 4 + 16 + 1 + 4 + secondLength];
        domain.CopyTo(preimage); var offset = domain.Length; preimage[offset++] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(preimage.AsSpan(offset), 16); offset += 4;
        if (!session.LiveSessionId.TryWriteBytes(preimage.AsSpan(offset))) throw new ArgumentException(); offset += 16;
        preimage[offset++] = 2; BinaryPrimitives.WriteUInt32BigEndian(preimage.AsSpan(offset), (uint)secondLength); offset += 4;
        if (position is { } p) BinaryPrimitives.WriteInt64BigEndian(preimage.AsSpan(offset), p.Sequence);
        else if (!operation.TryWriteBytes(preimage.AsSpan(offset))) throw new ArgumentException();
        Span<byte> digest = stackalloc byte[32]; SHA256.HashData(preimage, digest); Span<byte> id = stackalloc byte[16];
        digest[..16].CopyTo(id); if (id.IndexOfAnyExcept((byte)0) < 0) id[^1] = 1;
        return JournalFactId.FromValue(StableId128.FromBytes(id));
    }
}

internal static class GraphMediaPhysicalReleasePayloadRegistrationsV1
{
    internal const ushort CommandDiscriminator = 45, FactDiscriminator = 46;
    internal static readonly AuthorityPayloadRegistrationV1 Command = AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
        new BoundedAscii(GraphMediaPhysicalReleaseCodecsV1.CommandSchemaId), 1, 0, OwnerSliceId.S1,
        GraphMediaPhysicalReleaseCodecsV1.MaximumOuterBytes, ValidateCommand);
    internal static readonly AuthorityPayloadRegistrationV1 Fact = AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
        new BoundedAscii(GraphMediaPhysicalReleaseCodecsV1.FactSchemaId), 1, 0, OwnerSliceId.S1,
        GraphMediaPhysicalReleaseCodecsV1.MaximumOuterBytes, ValidateFact);
    private static bool ValidateCommand(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(payload, out var outer) && outer!.Session == session &&
        GraphMediaPhysicalReleaseCodecsV1.TryDecodeCommandBody(outer.BodyMemory, out var body) &&
        outer.ExpectedAuthority.Axes.Length == 1 &&
        outer.ExpectedAuthority.Axes[0].Value is AuthorityAxisValueV1.Graph graph &&
        graph.Value == body!.Residence.GraphGeneration &&
        (body!.ExpectedReleaseFact is null || body.ExpectedReleaseFact.Value.Session == session);
    private static bool ValidateFact(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) =>
        GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(payload, out var outer) && outer!.Session == session &&
        outer.ExpectedAuthority.Axes.Length == 1 && outer.ExpectedAuthority.Axes[0].Value is AuthorityAxisValueV1.Graph &&
        GraphMediaPhysicalReleaseCodecsV1.TryDecodeFactBody(outer.BodyMemory, out var body) && body!.CommandPosition.Session == session;
    internal static AuthorityPayloadAdmissionV1 ValidateCommandEnvelope(SessionAuthorityStampV1 session, ProposedAuthorityFactV1 proposal)
    {
        if (!GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(proposal.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaPhysicalReleaseCodecsV1.TryDecodeCommandBody(outer.BodyMemory, out var body) || body is null ||
            proposal.Correlation.OperationId != body.OperationId) return AuthorityPayloadAdmissionV1.InvalidPayload;
        return ValidateEnvelope(session, proposal, Command);
    }
    internal static AuthorityPayloadAdmissionV1 ValidateFactEnvelope(SessionAuthorityStampV1 session, ProposedAuthorityFactV1 proposal) =>
        proposal.Correlation.OperationId is null ? AuthorityPayloadAdmissionV1.InvalidPayload : ValidateEnvelope(session, proposal, Fact);
    private static AuthorityPayloadAdmissionV1 ValidateEnvelope(SessionAuthorityStampV1 session,
        ProposedAuthorityFactV1 proposal, AuthorityPayloadRegistrationV1 registration)
    {
        if (proposal.ThreadId is not null || proposal.Owner != OwnerSliceId.S1 || proposal.PayloadSchema != registration.Schema)
            return AuthorityPayloadAdmissionV1.InvalidPayload;
        var result = new AuthorityPayloadAdmissionRegistryV1([registration]).Validate(session, proposal, out _);
        return result == AuthorityPayloadAdmissionV1.Exact ? result : AuthorityPayloadAdmissionV1.InvalidPayload;
    }
}

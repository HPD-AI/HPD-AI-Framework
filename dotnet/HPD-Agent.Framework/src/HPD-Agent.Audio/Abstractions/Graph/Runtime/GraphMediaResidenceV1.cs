using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphMediaFanoutModeV1 : byte { Copy, TransferSingleDestination }
internal enum GraphMediaResidenceClassV1 : byte { Controlled, Opaque, Quarantine }
internal enum GraphMediaResidenceStateV1 : byte { Prepared, Visible, Releasing, Released, Unknown, Quarantined }
internal enum GraphMediaRepresentationArmV1 : byte { ResidentBytes, ResidentSamples, ResidentTimedBuffer }
internal enum GraphMediaResidenceResultV1 : byte
{ Prepared, IdempotentPrepared, Visible, Reconciled, Quarantined, IdempotentQuarantined, OpaqueAdmitted, IdempotentOpaqueAdmitted, InvalidRequest, StaleGeneration, AuthorityMismatch, ContradictoryDuplicate, SourceNotFound, NotOwner, AlreadyDisposed, CapacityMismatch, CapacityAssignmentConflict, ResidenceLimitReached, OperationReceiptLimitReached, WrongState, OutcomeUnknown }
internal enum GraphMediaFanoutResultV1 : byte
{ Prepared, IdempotentPrepared, Committed, Converged, Reconciled, Unwinding, InvalidRequest, StaleGeneration, ContradictoryDuplicate, SourceNotFound, NotOwner, AlreadyDisposed, DestinationOrderInvalid, DestinationCollision, ResidenceMismatch, CapacityMismatch, OwnerLimitReached, ResidenceLimitReached, OperationReceiptLimitReached, WrongState, OutcomeUnknown }

internal sealed record GraphMediaCapacityAssignmentV1(CapacityChargeV1 Charge, GraphMediaRepresentationArmV1 Arm);
internal sealed record GraphMediaControlledResidenceV1(OperationId OperationId, Hash256 RequestHash,
    StableId128 ResidenceId, StableId128 OwnerId, GraphMediaOwnerKeyV1 OwnerKey, GraphMediaBindingV1 Media,
    BoundedAscii DestinationNodeKey, ParticipantId ParticipantId, JournalPositionV1 BindingCommandPosition,
    JournalPositionV1 BindingFactPosition, JournalPositionV1 ReservationCommandPosition,
    JournalPositionV1 ReservationFactPosition, CapacityGrantId GrantId, JournalPositionV1 GrantedAt,
    JournalPositionV1 CurrentFact, Hash256 CoverageHashV2, Hash256 TopologyFingerprint,
    Hash256 ExecutableFingerprint, GraphMediaCapacityAssignmentV1 Assignment,
    GraphMediaResidenceClassV1 Class, GraphMediaResidenceStateV1 State);
internal sealed record GraphMediaResidenceReceiptV1(OperationId OperationId, Hash256 RequestHash,
    GraphMediaResidenceResultV1 Result);
internal sealed record GraphMediaQuarantineResidenceV1(OperationId OperationId, Hash256 RequestHash,
    StableId128 ResidenceId, StableId128 SourceResidenceId, StableId128 OwnerId,
    GraphMediaOwnerKeyV1 OwnerKey, GraphMediaBindingV1 Media, SchemaId SchemaId,
    CapacityGrantId GrantId, JournalPositionV1 GrantedAt, JournalPositionV1 CurrentFact,
    Hash256 CapacityProofHash, CapacityChargeV1 Charge, GraphMediaResidenceClassV1 Class,
    GraphMediaResidenceStateV1 State);
internal sealed record GraphMediaQuarantineIngressRequestV1(OperationId OperationId, Hash256 RequestHash,
    StableId128 ResidenceId, StableId128 SourceResidenceId, StableId128 OwnerId,
    SchemaId SchemaId, CapacityGrantSnapshotV1 Grant);
internal sealed record GraphMediaOpaqueResidenceV1(OperationId OperationId, Hash256 RequestHash,
    StableId128 ResidenceId, StableId128 SourceResidenceId, StableId128 OwnerId,
    GraphMediaOwnerKeyV1 OwnerKey, GraphMediaBindingV1 Media, ParticipantId ParticipantId,
    BoundedAscii FactoryKey, ProviderId ProviderId, Hash256 ParticipantCatalogFingerprint,
    Hash256 ProviderCatalogFingerprint, Hash256 ProviderContributionFingerprint,
    Hash256 ExternalReferenceFingerprint, ushort SubmittedOperations, ulong SubmittedBytes,
    DurationNs MaximumAge, MonotonicStampV1 AdmittedAt, LiveAudioOpaqueResidenceControlV1 Control,
    CapacityGrantId GrantId, JournalPositionV1 GrantedAt, JournalPositionV1 CurrentFact,
    Hash256 CapacityProofHash, IReadOnlyList<CapacityChargeV1> Charges,
    GraphMediaResidenceClassV1 Class, GraphMediaResidenceStateV1 State);
internal sealed record GraphMediaOpaqueIngressRequestV1(OperationId OperationId, Hash256 RequestHash,
    StableId128 ResidenceId, StableId128 SourceResidenceId, StableId128 OwnerId,
    Hash256 ExternalReferenceFingerprint, ushort SubmittedOperations, ulong SubmittedBytes,
    DurationNs MaximumAge, MonotonicStampV1 AdmittedAt,
    GraphMediaControlledResidenceRequestV1 AuthorityEvidence,
    LiveAudioParticipantCatalogManifestV1 ParticipantCatalog,
    LiveAudioParticipantFactoryRegistrationV1 SelectedRegistration,
    ProviderCatalogV1 ProviderCatalog, ProviderContributionV1 SelectedProvider,
    CapacityGrantSnapshotV1 Grant);
internal sealed record GraphMediaFanoutDestinationV1(StableId128 DestinationOwnerId,
    BoundedAscii DestinationNodeKey, GraphMediaControlledResidenceRequestV1 Residence);
internal sealed record GraphMediaFanoutRecordV1(OperationId OperationId, Hash256 RequestHash,
    StableId128 SourceOwnerId, GraphMediaFanoutModeV1 Mode,
    IReadOnlyList<GraphMediaFanoutDestinationV1> Destinations, GraphMediaFanoutResultV1 Result);
internal sealed record GraphMediaControlledResidenceRequestV1(OperationId OperationId, Hash256 RequestHash,
    StableId128 ResidenceId, StableId128 SourceOwnerId, StableId128 DestinationOwnerId,
    BoundedAscii DestinationNodeKey, GraphMediaRepresentationArmV1 Arm,
    GraphParticipantBindingResultV2.Bound BindingResult,
    GraphParticipantBindingFoldQueryResultV2.Bound FoldBound, GraphParticipantBindingPlanEvidenceV2 Evidence);
internal sealed record GraphMediaFanoutRequestV1(OperationId OperationId, Hash256 RequestHash,
    StableId128 SourceOwnerId, ulong ExpectedSourceVersion, GraphMediaFanoutModeV1 Mode,
    IReadOnlyList<GraphMediaFanoutDestinationV1> Destinations);
internal sealed record GraphMediaResidenceTransitionV1(GraphMediaResidenceResultV1 Result,
    GraphMediaResidenceLedgerV1 Ledger);
internal sealed record GraphMediaFanoutTransitionV1(GraphMediaFanoutResultV1 Result,
    GraphMediaResidenceLedgerV1 ResidenceLedger, GraphMediaOwnershipLedgerV1 OwnershipLedger,
    IReadOnlyList<StableId128> ReverseUnwindOwnerIds);

internal sealed class GraphMediaResidenceLedgerV1
{
    internal const int MaximumResidences = 96, MaximumControlled = 64, MaximumOpaque = 16,
        MaximumQuarantine = 16, MaximumFanoutOperations = 64, MaximumDestinations = 16;
    private readonly Dictionary<StableId128, GraphMediaControlledResidenceV1> _residences;
    private readonly Dictionary<StableId128, GraphMediaQuarantineResidenceV1> _quarantines;
    private readonly Dictionary<StableId128, GraphMediaOpaqueResidenceV1> _opaques;
    private readonly Dictionary<OperationId, GraphMediaResidenceReceiptV1> _receipts;
    private readonly Dictionary<OperationId, GraphMediaFanoutRecordV1> _fanouts;

    private GraphMediaResidenceLedgerV1(SessionAuthorityStampV1 session, GraphGenerationId graph,
        Dictionary<StableId128, GraphMediaControlledResidenceV1> residences,
        Dictionary<StableId128, GraphMediaQuarantineResidenceV1> quarantines,
        Dictionary<StableId128, GraphMediaOpaqueResidenceV1> opaques,
        Dictionary<OperationId, GraphMediaResidenceReceiptV1> receipts,
        Dictionary<OperationId, GraphMediaFanoutRecordV1> fanouts)
    { Session = session; GraphGeneration = graph; _residences = residences; _quarantines = quarantines; _opaques = opaques; _receipts = receipts; _fanouts = fanouts; }

    internal SessionAuthorityStampV1 Session { get; }
    internal GraphGenerationId GraphGeneration { get; }
    internal IReadOnlyDictionary<StableId128, GraphMediaControlledResidenceV1> Residences => new ReadOnlyDictionary<StableId128, GraphMediaControlledResidenceV1>(_residences);
    internal IReadOnlyDictionary<StableId128, GraphMediaQuarantineResidenceV1> Quarantines =>
        new ReadOnlyDictionary<StableId128, GraphMediaQuarantineResidenceV1>(_quarantines);
    internal IReadOnlyDictionary<StableId128, GraphMediaOpaqueResidenceV1> Opaques =>
        new ReadOnlyDictionary<StableId128, GraphMediaOpaqueResidenceV1>(_opaques);
    internal IReadOnlyCollection<GraphMediaResidenceReceiptV1> Receipts => Array.AsReadOnly(_receipts.Values.ToArray());
    internal IReadOnlyDictionary<OperationId, GraphMediaFanoutRecordV1> Fanouts => new ReadOnlyDictionary<OperationId, GraphMediaFanoutRecordV1>(_fanouts);
    internal Hash256 Fingerprint
    {
        get
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("hpd-s2-graph-media-residence-ledger-v1\0"u8);
            byte[] Id(StableId128 value) { var bytes = new byte[16]; if (!value.TryWriteBytes(bytes)) throw new InvalidOperationException(); return bytes; }
            byte[] Op(OperationId value) { var bytes = new byte[16]; if (!value.TryWriteBytes(bytes)) throw new InvalidOperationException(); return bytes; }
            byte[] Hash(Hash256 value) { var bytes = new byte[32]; if (!value.TryWriteBytes(bytes)) throw new InvalidOperationException(); return bytes; }
            byte[] Canonical(object value)
            {
                if (value is StableId128 stable) return Id(stable);
                if (value is OperationId operation) return Op(operation);
                if (value is Hash256 digest) return Hash(digest);
                if (value is BoundedAscii ascii) return Encoding.ASCII.GetBytes(ascii.ToString());
                if (value is JournalPositionV1 position) return AuthorityPositionCodecsV1.Encode(position);
                if (value is Enum enumeration) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, Convert.ToInt32(enumeration)); return bytes; }
                if (value is GraphMediaOwnerKeyV1 key) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(4); writer.WriteByteString(Canonical(key.Session.LiveSessionId)); writer.WriteByteString(Canonical(key.GraphGeneration)); writer.WriteByteString(Canonical(key.Session.RuntimeGenerationId)); writer.WriteByteString(Id(key.MediaId)); writer.WriteEndArray(); return writer.Encode(); }
                if (value is GraphMediaBindingV1 media) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(13); writer.WriteInt64(media.Start); writer.WriteInt64(media.EndExclusive); writer.WriteByteString(Id(media.FormatId)); writer.WriteUInt64(media.FormatRevision); writer.WriteUInt64(media.SampleRateHz); writer.WriteUInt64(media.ChannelCount); writer.WriteUInt64(media.BytesPerSample); writer.WriteByteString(Id(media.ClockId)); writer.WriteUInt64(media.ClockRevision); writer.WriteUInt64(media.Sequence); writer.WriteUInt64((byte)media.Discontinuity); writer.WriteInt64(media.ByteLength); writer.WriteInt64(media.FrameCount); writer.WriteEndArray(); return writer.Encode(); }
                if (value is CapacityChargeV1 charge) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(5); writer.WriteUInt64(charge.DimensionId.Value); writer.WriteByteString(CapacityScopeCanonicalCodecV1.Encode(charge.Scope)); writer.WriteInt64(charge.Amount); writer.WriteByteString(Canonical(charge.Purpose)); writer.WriteStartArray(charge.Window is CapacityChargeWindowV1.EndsAt ? 2 : 1); writer.WriteUInt64((ushort)charge.Window.Kind); if (charge.Window is CapacityChargeWindowV1.EndsAt endsAt) writer.WriteByteString(MonotonicStampV1Codec.Encode(endsAt.Value)); writer.WriteEndArray(); writer.WriteEndArray(); return writer.Encode(); }
                if (value is ushort u16) { var bytes = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes,u16); return bytes; }
                if (value is ulong u64) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes,u64); return bytes; }
                if (value is long i64) { var bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes,i64); return bytes; }
                var bytes16 = new byte[16]; var valid = value switch { SessionId x => x.TryWriteBytes(bytes16), LiveSessionId x => x.TryWriteBytes(bytes16), RuntimeGenerationId x => x.TryWriteBytes(bytes16), GraphGenerationId x => x.TryWriteBytes(bytes16), ParticipantId x => x.TryWriteBytes(bytes16), ProviderId x => x.TryWriteBytes(bytes16), CapacityGrantId x => x.TryWriteBytes(bytes16), CapacityPurposeId x => x.TryWriteBytes(bytes16), SchemaId x => x.TryWriteBytes(bytes16), _ => false }; return valid ? bytes16 : throw new ArgumentException("Unsupported canonical value.");
            }
            foreach (var row in _residences.OrderBy(x => Convert.ToHexString(Id(x.Key)), StringComparer.Ordinal))
            {
                var value = row.Value;
                Field(hash,"residenceOperation"u8,Op(value.OperationId)); Field(hash,"residenceRequest"u8,Hash(value.RequestHash)); Field(hash,"residenceId"u8,Id(value.ResidenceId)); Field(hash,"residenceOwner"u8,Id(value.OwnerId)); Field(hash,"residenceOwnerKey"u8,Canonical(value.OwnerKey)); Field(hash,"residenceMedia"u8,Canonical(value.Media)); Field(hash,"residenceNode"u8,Canonical(value.DestinationNodeKey)); Field(hash,"residenceParticipant"u8,Canonical(value.ParticipantId));
                Field(hash,"bindingCommand"u8,Canonical(value.BindingCommandPosition)); Field(hash,"bindingFact"u8,Canonical(value.BindingFactPosition)); Field(hash,"reservationCommand"u8,Canonical(value.ReservationCommandPosition)); Field(hash,"reservationFact"u8,Canonical(value.ReservationFactPosition)); Field(hash,"grantId"u8,Canonical(value.GrantId)); Field(hash,"grantedAt"u8,Canonical(value.GrantedAt)); Field(hash,"currentFact"u8,Canonical(value.CurrentFact)); Field(hash,"coverage"u8,Hash(value.CoverageHashV2)); Field(hash,"topology"u8,Hash(value.TopologyFingerprint)); Field(hash,"executable"u8,Hash(value.ExecutableFingerprint)); Field(hash,"assignmentCharge"u8,Canonical(value.Assignment.Charge)); Field(hash,"assignmentArm"u8,Canonical(value.Assignment.Arm)); Field(hash,"residenceClass"u8,Canonical(value.Class)); Field(hash,"residenceState"u8,Canonical(value.State));
            }
            foreach (var row in _quarantines.OrderBy(x => Convert.ToHexString(Id(x.Key)), StringComparer.Ordinal))
            {
                var value = row.Value;
                Field(hash,"quarantineOperation"u8,Op(value.OperationId)); Field(hash,"quarantineRequest"u8,Hash(value.RequestHash));
                Field(hash,"quarantineResidence"u8,Id(value.ResidenceId)); Field(hash,"quarantineSource"u8,Id(value.SourceResidenceId));
                Field(hash,"quarantineOwner"u8,Id(value.OwnerId)); Field(hash,"quarantineOwnerKey"u8,Canonical(value.OwnerKey));
                Field(hash,"quarantineMedia"u8,Canonical(value.Media)); Field(hash,"quarantineSchema"u8,Canonical(value.SchemaId));
                Field(hash,"quarantineGrant"u8,Canonical(value.GrantId)); Field(hash,"quarantineGrantedAt"u8,Canonical(value.GrantedAt));
                Field(hash,"quarantineCurrentFact"u8,Canonical(value.CurrentFact)); Field(hash,"quarantineCapacityProof"u8,Hash(value.CapacityProofHash));
                Field(hash,"quarantineCharge"u8,Canonical(value.Charge)); Field(hash,"quarantineClass"u8,Canonical(value.Class));
                Field(hash,"quarantineState"u8,Canonical(value.State));
            }
            foreach (var row in _opaques.OrderBy(x => Convert.ToHexString(Id(x.Key)), StringComparer.Ordinal))
            {
                var value = row.Value;
                Field(hash,"opaqueOperation"u8,Op(value.OperationId)); Field(hash,"opaqueRequest"u8,Hash(value.RequestHash));
                Field(hash,"opaqueResidence"u8,Id(value.ResidenceId)); Field(hash,"opaqueSource"u8,Id(value.SourceResidenceId));
                Field(hash,"opaqueOwner"u8,Id(value.OwnerId)); Field(hash,"opaqueOwnerKey"u8,Canonical(value.OwnerKey));
                Field(hash,"opaqueMedia"u8,Canonical(value.Media)); Field(hash,"opaqueParticipant"u8,Canonical(value.ParticipantId));
                Field(hash,"opaqueFactory"u8,Canonical(value.FactoryKey)); Field(hash,"opaqueProvider"u8,Canonical(value.ProviderId));
                Field(hash,"opaqueParticipantCatalog"u8,Hash(value.ParticipantCatalogFingerprint)); Field(hash,"opaqueProviderCatalog"u8,Hash(value.ProviderCatalogFingerprint));
                Field(hash,"opaqueProviderContribution"u8,Hash(value.ProviderContributionFingerprint)); Field(hash,"opaqueExternalReference"u8,Hash(value.ExternalReferenceFingerprint));
                Field(hash,"opaqueSubmittedOperations"u8,Canonical(value.SubmittedOperations)); Field(hash,"opaqueSubmittedBytes"u8,Canonical(value.SubmittedBytes));
                Field(hash,"opaqueMaximumAge"u8,Canonical(value.MaximumAge.Nanoseconds)); Field(hash,"opaqueAdmittedAt"u8,MonotonicStampV1Codec.Encode(value.AdmittedAt));
                Field(hash,"opaqueControl"u8,Canonical(value.Control)); Field(hash,"opaqueGrant"u8,Canonical(value.GrantId));
                Field(hash,"opaqueGrantedAt"u8,Canonical(value.GrantedAt)); Field(hash,"opaqueCurrentFact"u8,Canonical(value.CurrentFact));
                Field(hash,"opaqueCapacityProof"u8,Hash(value.CapacityProofHash)); foreach (var charge in value.Charges) Field(hash,"opaqueCharge"u8,Canonical(charge));
                Field(hash,"opaqueClass"u8,Canonical(value.Class)); Field(hash,"opaqueState"u8,Canonical(value.State));
            }
            foreach (var receipt in _receipts.OrderBy(x => Convert.ToHexString(Op(x.Key)), StringComparer.Ordinal))
            { Field(hash, "receiptOperation"u8, Op(receipt.Key)); Field(hash, "receiptRequest"u8, Hash(receipt.Value.RequestHash)); Field(hash, "receiptResult"u8, [(byte)receipt.Value.Result]); }
            foreach (var fanout in _fanouts.OrderBy(x => Convert.ToHexString(Op(x.Key)), StringComparer.Ordinal))
            {
                var value = fanout.Value;
                Field(hash,"fanoutOperation"u8,Op(value.OperationId)); Field(hash,"fanoutRequest"u8,Hash(value.RequestHash)); Field(hash,"fanoutSource"u8,Id(value.SourceOwnerId)); Field(hash,"fanoutMode"u8,Canonical(value.Mode)); Field(hash,"fanoutResult"u8,Canonical(value.Result));
                foreach (var destination in value.Destinations)
                {
                    var residence = destination.Residence;
                    Field(hash,"destinationOwner"u8,Id(destination.DestinationOwnerId)); Field(hash,"destinationNode"u8,Canonical(destination.DestinationNodeKey)); Field(hash,"destinationResidenceOperation"u8,Op(residence.OperationId)); Field(hash,"destinationResidenceRequest"u8,Hash(residence.RequestHash)); Field(hash,"destinationResidenceId"u8,Id(residence.ResidenceId)); Field(hash,"destinationResidenceSource"u8,Id(residence.SourceOwnerId)); Field(hash,"destinationResidenceOwner"u8,Id(residence.DestinationOwnerId)); Field(hash,"destinationResidenceNode"u8,Canonical(residence.DestinationNodeKey)); Field(hash,"destinationResidenceArm"u8,Canonical(residence.Arm)); Field(hash,"destinationBindingCommand"u8,Canonical(residence.BindingResult.CommandPosition)); Field(hash,"destinationBindingFact"u8,Canonical(residence.BindingResult.FactPosition)); Field(hash,"destinationReservationCommand"u8,Canonical(residence.Evidence.PreGrantPlan.ReservationCommandPosition)); Field(hash,"destinationReservationFact"u8,Canonical(residence.Evidence.PreGrantPlan.ReservationFactPosition)); Field(hash,"destinationGrant"u8,Canonical(residence.Evidence.GrantId)); Field(hash,"destinationCoverage"u8,Hash(residence.Evidence.CoverageHashV2)); Field(hash,"destinationTopology"u8,Hash(residence.Evidence.TopologyFingerprint)); Field(hash,"destinationExecutable"u8,Hash(residence.Evidence.ExecutableFingerprint));
                }
            }
            return Hash256.FromBytes(hash.GetHashAndReset());
        }
    }

    internal static GraphMediaResidenceLedgerV1 Create(SessionAuthorityStampV1 session, GraphGenerationId graph)
    {
        if (!session.IsValid || !graph.IsValid) throw new ArgumentException("Valid authority is required.");
        return new(session, graph, [], [], [], [], []);
    }

    internal GraphMediaResidenceTransitionV1 PrepareControlled(GraphMediaControlledResidenceRequestV1 request,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        if (request is null || ownership is null || !request.OperationId.IsValid || request.RequestHash == default ||
            request.ResidenceId.Equals(default) || request.SourceOwnerId.Equals(default) ||
            request.DestinationOwnerId.Equals(default) || request.SourceOwnerId.Equals(request.DestinationOwnerId) ||
            !request.DestinationNodeKey.IsValid || !Enum.IsDefined(request.Arm))
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (ownership.Session != Session || ownership.GraphGeneration != GraphGeneration)
            return ResidenceFail(GraphMediaResidenceResultV1.StaleGeneration);
        if (_receipts.TryGetValue(request.OperationId, out var retry))
            return ResidenceFail(retry.RequestHash == request.RequestHash
                ? GraphMediaResidenceResultV1.IdempotentPrepared : GraphMediaResidenceResultV1.ContradictoryDuplicate);
        if (_residences.ContainsKey(request.ResidenceId))
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (!ownership.Owners.TryGetValue(request.SourceOwnerId, out var owner))
            return ResidenceFail(GraphMediaResidenceResultV1.SourceNotFound);
        if (owner.State == GraphMediaOwnerStateV1.Transferred)
            return ResidenceFail(GraphMediaResidenceResultV1.NotOwner);
        if (owner.State == GraphMediaOwnerStateV1.Disposed)
            return ResidenceFail(GraphMediaResidenceResultV1.AlreadyDisposed);
        var authority = AuthenticateControlled(request, owner);
        if (authority != GraphMediaResidenceResultV1.Prepared) return ResidenceFail(authority);
        var assignmentResult = SelectAssignment(request, owner.Media, out var assignment);
        if (assignmentResult != GraphMediaResidenceResultV1.Prepared) return ResidenceFail(assignmentResult);
        if (ResidenceHash(request, owner, assignment!) != request.RequestHash)
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (_residences.Values.Any(x => x.State != GraphMediaResidenceStateV1.Released && x.Assignment == assignment))
            return ResidenceFail(GraphMediaResidenceResultV1.CapacityAssignmentConflict);
        if (_residences.Count + _quarantines.Count + _opaques.Count >= MaximumResidences ||
            _residences.Values.Count(x => x.Class == GraphMediaResidenceClassV1.Controlled) >= MaximumControlled)
            return ResidenceFail(GraphMediaResidenceResultV1.ResidenceLimitReached);
        if (_receipts.Count >= GraphMediaOwnershipLedgerV1.MaximumReceipts)
            return ResidenceFail(GraphMediaResidenceResultV1.OperationReceiptLimitReached);
        var row = new GraphMediaControlledResidenceV1(request.OperationId, request.RequestHash, request.ResidenceId,
            request.DestinationOwnerId, owner.Key, owner.Media, request.DestinationNodeKey,
            request.BindingResult.Binding.ParticipantId, request.BindingResult.CommandPosition,
            request.BindingResult.FactPosition, request.Evidence.PreGrantPlan.ReservationCommandPosition,
            request.Evidence.PreGrantPlan.ReservationFactPosition, request.Evidence.GrantId,
            request.Evidence.GrantedAt, request.Evidence.CurrentFact, request.Evidence.CoverageHashV2,
            request.Evidence.TopologyFingerprint, request.Evidence.ExecutableFingerprint, assignment!,
            GraphMediaResidenceClassV1.Controlled, GraphMediaResidenceStateV1.Prepared);
        var residences = new Dictionary<StableId128, GraphMediaControlledResidenceV1>(_residences) { [request.ResidenceId] = row };
        var receipts = new Dictionary<OperationId, GraphMediaResidenceReceiptV1>(_receipts)
        { [request.OperationId] = new(request.OperationId, request.RequestHash, GraphMediaResidenceResultV1.Prepared) };
        return new(GraphMediaResidenceResultV1.Prepared, Next(residences, receipts, new(_fanouts)));
    }

    internal GraphMediaResidenceTransitionV1 Quarantine(GraphMediaQuarantineIngressRequestV1 request,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        if (request is null || ownership is null || !request.OperationId.IsValid || request.RequestHash == default ||
            request.ResidenceId.Equals(default) || request.SourceResidenceId.Equals(default) ||
            request.ResidenceId.Equals(request.SourceResidenceId) || request.OwnerId.Equals(default) ||
            !request.SchemaId.IsValid || request.Grant is null)
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (ownership.Session != Session || ownership.GraphGeneration != GraphGeneration ||
            request.Grant.Authority.Session != Session || request.Grant.GrantedAt.Session != Session ||
            request.Grant.CurrentFact.Session != Session)
            return ResidenceFail(GraphMediaResidenceResultV1.StaleGeneration);
        var graphs = request.Grant.Authority.Axes.Where(x => x.AxisId == AuthorityAxisId.Graph &&
            x.Value is AuthorityAxisValueV1.Graph).Select(x => ((AuthorityAxisValueV1.Graph)x.Value).Value).ToArray();
        if (graphs.Length != 1 || graphs[0] != GraphGeneration)
            return ResidenceFail(GraphMediaResidenceResultV1.StaleGeneration);
        if (_receipts.TryGetValue(request.OperationId, out var retry))
            return ResidenceFail(retry.RequestHash == request.RequestHash && retry.Result == GraphMediaResidenceResultV1.Quarantined
                ? GraphMediaResidenceResultV1.IdempotentQuarantined : GraphMediaResidenceResultV1.ContradictoryDuplicate);
        if (_residences.ContainsKey(request.ResidenceId) || _quarantines.ContainsKey(request.ResidenceId))
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (!_residences.TryGetValue(request.SourceResidenceId, out var source) ||
            source.State != GraphMediaResidenceStateV1.Unknown || !source.OwnerId.Equals(request.OwnerId))
            return ResidenceFail(GraphMediaResidenceResultV1.WrongState);
        if (!ownership.Owners.TryGetValue(request.OwnerId, out var owner))
            return ResidenceFail(GraphMediaResidenceResultV1.SourceNotFound);
        if (owner.State != GraphMediaOwnerStateV1.Owned || owner.Key != source.OwnerKey || owner.Media != source.Media)
            return ResidenceFail(GraphMediaResidenceResultV1.NotOwner);
        if (source.Media.ByteLength is <= 0 or > 1_048_576 ||
            request.Grant.State is not (CapacityGrantStateV1.Reserved or CapacityGrantStateV1.Active))
            return ResidenceFail(GraphMediaResidenceResultV1.CapacityMismatch);
        var candidates = request.Grant.Balances.Where(x => x.Charge.DimensionId.Value == 12 &&
            x.Charge.Scope.Kind == CapacityScopeKindV1.Schema &&
            x.Charge.Scope.Subject is CapacitySubjectV1.Schema schema && schema.Value == request.SchemaId).ToArray();
        if (candidates.Length != 1) return ResidenceFail(GraphMediaResidenceResultV1.CapacityMismatch);
        var balance = candidates[0]; var charge = balance.Charge;
        long available;
        try { available = checked(balance.Unactivated + balance.Active); }
        catch (OverflowException) { return ResidenceFail(GraphMediaResidenceResultV1.CapacityMismatch); }
        if (charge.Amount != source.Media.ByteLength || available != charge.Amount ||
            balance.Released != 0 || balance.Consumed != 0 || balance.AgedOut != 0 || balance.Revoked != 0 ||
            balance.ExplicitlyUnknown != 0 || charge.Window is not CapacityChargeWindowV1.NoWindow)
            return ResidenceFail(GraphMediaResidenceResultV1.CapacityMismatch);
        var proofHash = QuarantineCapacityProofHash(request.Grant);
        if (QuarantineHash(request, owner, charge, proofHash) != request.RequestHash)
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (_residences.Count + _quarantines.Count + _opaques.Count >= MaximumResidences || _quarantines.Count >= MaximumQuarantine)
            return ResidenceFail(GraphMediaResidenceResultV1.ResidenceLimitReached);
        if (_receipts.Count >= GraphMediaOwnershipLedgerV1.MaximumReceipts)
            return ResidenceFail(GraphMediaResidenceResultV1.OperationReceiptLimitReached);
        var quarantines = new Dictionary<StableId128, GraphMediaQuarantineResidenceV1>(_quarantines)
        {
            [request.ResidenceId] = new(request.OperationId, request.RequestHash, request.ResidenceId,
                request.SourceResidenceId, request.OwnerId, owner.Key, owner.Media, request.SchemaId,
                request.Grant.GrantId, request.Grant.GrantedAt, request.Grant.CurrentFact, proofHash, charge,
                GraphMediaResidenceClassV1.Quarantine, GraphMediaResidenceStateV1.Quarantined)
        };
        var receipts = new Dictionary<OperationId, GraphMediaResidenceReceiptV1>(_receipts)
        { [request.OperationId] = new(request.OperationId, request.RequestHash, GraphMediaResidenceResultV1.Quarantined) };
        return new(GraphMediaResidenceResultV1.Quarantined,
            Next(new(_residences), quarantines, receipts, new(_fanouts)));
    }

    internal GraphMediaResidenceTransitionV1 AdmitOpaque(GraphMediaOpaqueIngressRequestV1 request,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        if (request is null || ownership is null || !request.OperationId.IsValid || request.RequestHash == default ||
            request.ResidenceId.Equals(default) || request.SourceResidenceId.Equals(default) ||
            request.ResidenceId.Equals(request.SourceResidenceId) || request.OwnerId.Equals(default) ||
            request.ExternalReferenceFingerprint == default || request.SubmittedOperations == 0 ||
            request.SubmittedBytes == 0 || request.MaximumAge.Nanoseconds <= 0 || !request.AdmittedAt.IsValid ||
            request.AuthorityEvidence is null || request.ParticipantCatalog is null || request.SelectedRegistration is null ||
            request.ProviderCatalog is null || request.SelectedProvider is null || request.Grant is null)
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (ownership.Session != Session || ownership.GraphGeneration != GraphGeneration ||
            request.Grant.Authority.Session != Session || request.Grant.GrantedAt.Session != Session ||
            request.Grant.CurrentFact.Session != Session)
            return ResidenceFail(GraphMediaResidenceResultV1.StaleGeneration);
        var graphs = request.Grant.Authority.Axes.Where(x => x.AxisId == AuthorityAxisId.Graph &&
            x.Value is AuthorityAxisValueV1.Graph).Select(x => ((AuthorityAxisValueV1.Graph)x.Value).Value).ToArray();
        if (graphs.Length != 1 || graphs[0] != GraphGeneration)
            return ResidenceFail(GraphMediaResidenceResultV1.StaleGeneration);
        if (_receipts.TryGetValue(request.OperationId, out var retry))
            return ResidenceFail(retry.RequestHash == request.RequestHash && retry.Result == GraphMediaResidenceResultV1.OpaqueAdmitted
                ? GraphMediaResidenceResultV1.IdempotentOpaqueAdmitted : GraphMediaResidenceResultV1.ContradictoryDuplicate);
        if (_residences.ContainsKey(request.ResidenceId) || _quarantines.ContainsKey(request.ResidenceId) || _opaques.ContainsKey(request.ResidenceId))
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (!_residences.TryGetValue(request.SourceResidenceId, out var source) ||
            source.State != GraphMediaResidenceStateV1.Visible || !source.OwnerId.Equals(request.OwnerId))
            return ResidenceFail(GraphMediaResidenceResultV1.WrongState);
        if (!ownership.Owners.TryGetValue(request.OwnerId, out var owner))
            return ResidenceFail(GraphMediaResidenceResultV1.SourceNotFound);
        if (owner.State != GraphMediaOwnerStateV1.Owned || owner.Key != source.OwnerKey || owner.Media != source.Media)
            return ResidenceFail(GraphMediaResidenceResultV1.NotOwner);
        var evidence = request.AuthorityEvidence;
        if (!evidence.DestinationOwnerId.Equals(request.OwnerId) || !evidence.ResidenceId.Equals(request.SourceResidenceId) ||
            evidence.BindingResult.CommandPosition != source.BindingCommandPosition ||
            evidence.BindingResult.FactPosition != source.BindingFactPosition ||
            evidence.Evidence.PreGrantPlan.ReservationCommandPosition != source.ReservationCommandPosition ||
            evidence.Evidence.PreGrantPlan.ReservationFactPosition != source.ReservationFactPosition ||
            evidence.BindingResult.Binding.ParticipantId != source.ParticipantId ||
            evidence.DestinationNodeKey != source.DestinationNodeKey || AuthenticateControlled(evidence, owner) != GraphMediaResidenceResultV1.Prepared)
            return ResidenceFail(GraphMediaResidenceResultV1.AuthorityMismatch);
        if (!request.ParticipantCatalog.TryGet(request.SelectedRegistration.Descriptor.FactoryKey, out var registered) ||
            !RegistrationEquals(registered, request.SelectedRegistration) ||
            request.SelectedRegistration.Descriptor.FactoryKey != evidence.BindingResult.Binding.ParticipantFactoryKey ||
            request.SelectedRegistration.OpaqueResidenceQualification is not { } qualification ||
            qualification.ProviderId != request.SelectedProvider.ProviderId ||
            !request.ProviderCatalog.Contributions.Any(value => value == request.SelectedProvider))
            return ResidenceFail(GraphMediaResidenceResultV1.AuthorityMismatch);
        if (request.SubmittedOperations > qualification.MaximumOutstandingOperations ||
            request.SubmittedBytes > qualification.MaximumSubmittedBytes ||
            request.MaximumAge.Nanoseconds > qualification.MaximumAge.Nanoseconds)
            return ResidenceFail(GraphMediaResidenceResultV1.CapacityMismatch);
        ulong expiryNanoseconds;
        try { expiryNanoseconds = checked(request.AdmittedAt.Nanoseconds + (ulong)request.MaximumAge.Nanoseconds); }
        catch (OverflowException) { return ResidenceFail(GraphMediaResidenceResultV1.CapacityMismatch); }
        var expectedExpiry = new MonotonicStampV1(request.AdmittedAt.ClockDomainId, request.AdmittedAt.BootId, expiryNanoseconds);
        if (request.Grant.State is not (CapacityGrantStateV1.Reserved or CapacityGrantStateV1.Active) ||
            request.Grant.ExpiresAt is not CapacityGrantExpiryV1.At grantExpiry || grantExpiry.Value != expectedExpiry ||
            request.Grant.Balances.Count != 2)
            return ResidenceFail(GraphMediaResidenceResultV1.CapacityMismatch);
        var providerBalances = request.Grant.Balances.Where(x => x.Charge.DimensionId.Value == 6 &&
            x.Charge.Scope.Subject is CapacitySubjectV1.Provider provider && provider.Value == qualification.ProviderId).ToArray();
        var byteBalances = request.Grant.Balances.Where(x => x.Charge.DimensionId.Value == 2 &&
            x.Charge.Scope.Subject is CapacitySubjectV1.Operation operation && operation.Value == request.OperationId).ToArray();
        if (providerBalances.Length != 1 || byteBalances.Length != 1 ||
            !OpaqueBalance(providerBalances[0], request.SubmittedOperations) ||
            !OpaqueBalance(byteBalances[0], checked((long)request.SubmittedBytes)))
            return ResidenceFail(GraphMediaResidenceResultV1.CapacityMismatch);
        var proofHash = OpaqueCapacityProofHash(request.Grant);
        var contributionHash = ProviderContributionV1Codec.ComputeIntegrityHash(request.SelectedProvider);
        if (OpaqueHash(request, owner, qualification, contributionHash, proofHash) != request.RequestHash)
            return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        if (_residences.Count + _quarantines.Count + _opaques.Count >= MaximumResidences || _opaques.Count >= MaximumOpaque)
            return ResidenceFail(GraphMediaResidenceResultV1.ResidenceLimitReached);
        if (_receipts.Count >= GraphMediaOwnershipLedgerV1.MaximumReceipts)
            return ResidenceFail(GraphMediaResidenceResultV1.OperationReceiptLimitReached);
        var charges = Array.AsReadOnly(request.Grant.Balances.Select(static value => value.Charge).OrderBy(static value => value.DimensionId.Value).ToArray());
        var opaques = new Dictionary<StableId128, GraphMediaOpaqueResidenceV1>(_opaques)
        {
            [request.ResidenceId] = new(request.OperationId, request.RequestHash, request.ResidenceId,
                request.SourceResidenceId, request.OwnerId, owner.Key, owner.Media, source.ParticipantId,
                request.SelectedRegistration.Descriptor.FactoryKey, qualification.ProviderId,
                request.ParticipantCatalog.Fingerprint, request.ProviderCatalog.Fingerprint, contributionHash,
                request.ExternalReferenceFingerprint, request.SubmittedOperations, request.SubmittedBytes,
                request.MaximumAge, request.AdmittedAt, qualification.Control, request.Grant.GrantId,
                request.Grant.GrantedAt, request.Grant.CurrentFact, proofHash, charges,
                GraphMediaResidenceClassV1.Opaque, GraphMediaResidenceStateV1.Prepared)
        };
        var receipts = new Dictionary<OperationId, GraphMediaResidenceReceiptV1>(_receipts)
        { [request.OperationId] = new(request.OperationId, request.RequestHash, GraphMediaResidenceResultV1.OpaqueAdmitted) };
        return new(GraphMediaResidenceResultV1.OpaqueAdmitted,
            Next(new(_residences), new(_quarantines), opaques, receipts, new(_fanouts)));
    }

    internal GraphMediaResidenceTransitionV1 MakeVisible(OperationId operation, Hash256 requestHash,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        if (!operation.IsValid || requestHash == default || ownership is null) return ResidenceFail(GraphMediaResidenceResultV1.InvalidRequest);
        var row = _residences.Values.SingleOrDefault(x => x.OperationId == operation);
        if (row is null || row.RequestHash != requestHash) return ResidenceFail(GraphMediaResidenceResultV1.WrongState);
        if (row.State == GraphMediaResidenceStateV1.Visible) return ResidenceFail(GraphMediaResidenceResultV1.Visible);
        if (row.State != GraphMediaResidenceStateV1.Prepared || ownership.Session != Session || ownership.GraphGeneration != GraphGeneration ||
            !ownership.Owners.TryGetValue(row.OwnerId, out var owner) || owner.State != GraphMediaOwnerStateV1.Owned || owner.Key != row.OwnerKey || owner.Media != row.Media)
            return ResidenceFail(GraphMediaResidenceResultV1.WrongState);
        var residences = new Dictionary<StableId128, GraphMediaControlledResidenceV1>(_residences)
        { [row.ResidenceId] = row with { State = GraphMediaResidenceStateV1.Visible } };
        return new(GraphMediaResidenceResultV1.Visible, Next(residences, new(_receipts), new(_fanouts)));
    }

    internal GraphMediaResidenceTransitionV1 LoseOutcome(OperationId operation, Hash256 requestHash)
    {
        var row = _residences.Values.SingleOrDefault(x => x.OperationId == operation && x.RequestHash == requestHash);
        if (row is null || row.State is not (GraphMediaResidenceStateV1.Prepared or GraphMediaResidenceStateV1.Visible))
            return ResidenceFail(GraphMediaResidenceResultV1.WrongState);
        var residences = new Dictionary<StableId128, GraphMediaControlledResidenceV1>(_residences)
        { [row.ResidenceId] = row with { State = GraphMediaResidenceStateV1.Unknown } };
        return new(GraphMediaResidenceResultV1.OutcomeUnknown, Next(residences, new(_receipts), new(_fanouts)));
    }

    internal GraphMediaResidenceTransitionV1 Reconcile(OperationId operation, Hash256 requestHash, bool visible,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        var row = _residences.Values.SingleOrDefault(x => x.OperationId == operation && x.RequestHash == requestHash);
        if (row is null || row.State != GraphMediaResidenceStateV1.Unknown || ownership is null)
            return ResidenceFail(GraphMediaResidenceResultV1.WrongState);
        var hasOwner = ownership.Owners.TryGetValue(row.OwnerId, out var owner);
        if (hasOwner && (owner!.State != GraphMediaOwnerStateV1.Owned || owner.Key != row.OwnerKey || owner.Media != row.Media))
            return ResidenceFail(GraphMediaResidenceResultV1.WrongState);
        var owned = hasOwner;
        if (visible != owned) return ResidenceFail(GraphMediaResidenceResultV1.WrongState);
        var residences = new Dictionary<StableId128, GraphMediaControlledResidenceV1>(_residences)
        { [row.ResidenceId] = row with { State = visible ? GraphMediaResidenceStateV1.Visible : GraphMediaResidenceStateV1.Prepared } };
        return new(GraphMediaResidenceResultV1.Reconciled, Next(residences, new(_receipts), new(_fanouts)));
    }

    internal GraphMediaFanoutTransitionV1 PrepareFanout(GraphMediaFanoutRequestV1 request,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (request is null || !request.OperationId.IsValid || request.RequestHash == default ||
            request.SourceOwnerId.Equals(default) || !Enum.IsDefined(request.Mode) || request.Destinations is null ||
            request.Destinations.Count is < 1 or > MaximumDestinations ||
            request.Mode == GraphMediaFanoutModeV1.TransferSingleDestination && request.Destinations.Count != 1)
            return FanoutFail(GraphMediaFanoutResultV1.InvalidRequest, ownership!);
        if (ownership.Session != Session || ownership.GraphGeneration != GraphGeneration)
            return FanoutFail(GraphMediaFanoutResultV1.StaleGeneration, ownership);
        if (_fanouts.TryGetValue(request.OperationId, out var retry))
        {
            if (retry.RequestHash != request.RequestHash)
                return FanoutFail(GraphMediaFanoutResultV1.ContradictoryDuplicate, ownership);
            var ownershipState = FanoutOwnershipState(retry, ownership);
            var expectedState = retry.Result is GraphMediaFanoutResultV1.Committed or GraphMediaFanoutResultV1.Reconciled ? 1 : 0;
            if (ownershipState < 0 || (retry.Result != GraphMediaFanoutResultV1.OutcomeUnknown && ownershipState != expectedState))
                return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
            var replay = retry.Result == GraphMediaFanoutResultV1.Prepared
                ? GraphMediaFanoutResultV1.IdempotentPrepared : retry.Result;
            return FanoutFail(replay, ownership);
        }
        if (!ownership.Owners.TryGetValue(request.SourceOwnerId, out var source)) return FanoutFail(GraphMediaFanoutResultV1.SourceNotFound, ownership);
        if (source.State == GraphMediaOwnerStateV1.Transferred) return FanoutFail(GraphMediaFanoutResultV1.NotOwner, ownership);
        if (source.State == GraphMediaOwnerStateV1.Disposed) return FanoutFail(GraphMediaFanoutResultV1.AlreadyDisposed, ownership);
        var ordered = request.Destinations.Select(x => x.DestinationOwnerId).ToArray();
        var currentBytes = new byte[16]; var priorBytes = new byte[16];
        for (var i = 0; i < ordered.Length; i++)
        {
            ordered[i].TryWriteBytes(currentBytes);
            if (i > 0) { ordered[i - 1].TryWriteBytes(priorBytes); if (priorBytes.AsSpan().SequenceCompareTo(currentBytes) >= 0) return FanoutFail(ordered[i - 1].Equals(ordered[i]) ? GraphMediaFanoutResultV1.DestinationCollision : GraphMediaFanoutResultV1.DestinationOrderInvalid, ownership); }
            if (ownership.Owners.ContainsKey(ordered[i])) return FanoutFail(GraphMediaFanoutResultV1.DestinationCollision, ownership);
        }
        if (request.Destinations.Any(x => !x.Residence.DestinationOwnerId.Equals(x.DestinationOwnerId) || x.Residence.DestinationNodeKey != x.DestinationNodeKey || !x.Residence.SourceOwnerId.Equals(request.SourceOwnerId)))
            return FanoutFail(GraphMediaFanoutResultV1.ResidenceMismatch, ownership);
        if (request.Destinations.Select(x => x.Residence.ResidenceId).Distinct().Count() != request.Destinations.Count)
            return FanoutFail(GraphMediaFanoutResultV1.ResidenceMismatch, ownership);
        if (request.Destinations.Select(x => x.Residence.OperationId).Distinct().Count() != request.Destinations.Count)
            return FanoutFail(GraphMediaFanoutResultV1.ResidenceMismatch, ownership);
        if (FanoutHash(request, source) != request.RequestHash) return FanoutFail(GraphMediaFanoutResultV1.InvalidRequest, ownership);
        var ledger = this;
        foreach (var destination in request.Destinations)
        {
            var prepared = ledger.PrepareControlled(destination.Residence, ownership);
            if (prepared.Result == GraphMediaResidenceResultV1.CapacityMismatch || prepared.Result == GraphMediaResidenceResultV1.CapacityAssignmentConflict)
                return FanoutFail(GraphMediaFanoutResultV1.CapacityMismatch, ownership);
            if (prepared.Result == GraphMediaResidenceResultV1.ResidenceLimitReached) return FanoutFail(GraphMediaFanoutResultV1.ResidenceLimitReached, ownership);
            if (prepared.Result != GraphMediaResidenceResultV1.Prepared && prepared.Result != GraphMediaResidenceResultV1.IdempotentPrepared)
                return FanoutFail(GraphMediaFanoutResultV1.ResidenceMismatch, ownership);
            ledger = prepared.Ledger;
        }
        if (ledger._fanouts.Count >= MaximumFanoutOperations) return FanoutFail(GraphMediaFanoutResultV1.OperationReceiptLimitReached, ownership);
        var fanouts = new Dictionary<OperationId, GraphMediaFanoutRecordV1>(ledger._fanouts)
        { [request.OperationId] = new(request.OperationId, request.RequestHash, request.SourceOwnerId, request.Mode, Array.AsReadOnly(request.Destinations.ToArray()), GraphMediaFanoutResultV1.Prepared) };
        return new(GraphMediaFanoutResultV1.Prepared, ledger.Next(new(ledger._residences), new(ledger._receipts), fanouts), ownership, []);
    }

    internal GraphMediaFanoutTransitionV1 CommitFanout(OperationId operation, Hash256 requestHash,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (!_fanouts.TryGetValue(operation, out var fanout) || fanout.RequestHash != requestHash)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        if (fanout.Result == GraphMediaFanoutResultV1.Committed)
            return FanoutOwnershipState(fanout, ownership) == 1
                ? new(GraphMediaFanoutResultV1.Committed, this, ownership, [])
                : FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        if (fanout.Result != GraphMediaFanoutResultV1.Prepared)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        GraphMediaOwnershipLedgerV1 nextOwnership;
        if (fanout.Mode == GraphMediaFanoutModeV1.Copy)
        {
            var copy = ownership.CopyOwners(Session, GraphGeneration, fanout.SourceOwnerId, fanout.Destinations.Select(x => x.DestinationOwnerId).ToArray());
            if (copy.Result != GraphMediaOwnershipBatchCopyResultV1.Copied) return FanoutFail(copy.Result == GraphMediaOwnershipBatchCopyResultV1.OwnerLimitReached ? GraphMediaFanoutResultV1.OwnerLimitReached : GraphMediaFanoutResultV1.WrongState, ownership);
            nextOwnership = copy.Ledger;
        }
        else
        {
            if (!ownership.Owners.TryGetValue(fanout.SourceOwnerId, out var source)) return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
            var destination = fanout.Destinations[0].DestinationOwnerId;
            var hash = GraphMediaOwnershipCodecV1.OwnerTransition(operation, GraphMediaOwnerActionV1.Transfer, fanout.SourceOwnerId, destination, source.Key, source.Media, source.Version, out _);
            var transfer = ownership.Transition(Session, GraphGeneration, operation, GraphMediaOwnerActionV1.Transfer, fanout.SourceOwnerId, destination, source.Version, hash);
            if (transfer.Result != GraphMediaOwnerTransitionResultV1.Transferred) return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
            nextOwnership = transfer.Ledger;
        }
        var residences = new Dictionary<StableId128, GraphMediaControlledResidenceV1>(_residences);
        foreach (var destination in fanout.Destinations)
        {
            if (!residences.TryGetValue(destination.Residence.ResidenceId, out var row) || row.State != GraphMediaResidenceStateV1.Prepared)
                return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
            residences[row.ResidenceId] = row with { State = GraphMediaResidenceStateV1.Visible };
        }
        var fanouts = new Dictionary<OperationId, GraphMediaFanoutRecordV1>(_fanouts)
        { [operation] = fanout with { Result = GraphMediaFanoutResultV1.Committed } };
        return new(GraphMediaFanoutResultV1.Committed, Next(residences, new(_receipts), fanouts), nextOwnership, []);
    }

    internal GraphMediaFanoutTransitionV1 FailFanout(OperationId operation, Hash256 requestHash,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (!_fanouts.TryGetValue(operation, out var row) || row.RequestHash != requestHash)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        if (row.Result == GraphMediaFanoutResultV1.Unwinding)
            return FanoutOwnershipState(row, ownership) == 0
                ? new(GraphMediaFanoutResultV1.Unwinding, this, ownership,
                    Array.AsReadOnly(row.Destinations.Reverse().Select(x => x.DestinationOwnerId).ToArray()))
                : FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        if (row.Result != GraphMediaFanoutResultV1.Prepared)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        if (FanoutOwnershipState(row, ownership) != 0)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        var reverse = row.Destinations.Reverse().Select(x => x.DestinationOwnerId).ToArray();
        var fanouts = new Dictionary<OperationId, GraphMediaFanoutRecordV1>(_fanouts) { [operation] = row with { Result = GraphMediaFanoutResultV1.Unwinding } };
        return new(GraphMediaFanoutResultV1.Unwinding, Next(new(_residences), new(_receipts), fanouts), ownership, Array.AsReadOnly(reverse));
    }

    internal GraphMediaFanoutTransitionV1 ReconcileFanout(OperationId operation, Hash256 requestHash,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (!_fanouts.TryGetValue(operation, out var row) || row.RequestHash != requestHash)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        if (row.Result is GraphMediaFanoutResultV1.Reconciled or GraphMediaFanoutResultV1.Converged)
        {
            var terminalState = FanoutOwnershipState(row, ownership);
            var expectedState = row.Result == GraphMediaFanoutResultV1.Reconciled ? 1 : 0;
            return terminalState == expectedState
                ? new(row.Result, this, ownership, [])
                : FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        }
        if (row.Result != GraphMediaFanoutResultV1.OutcomeUnknown)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        var durable = FanoutOwnershipState(row, ownership);
        if (durable < 0)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        var result = durable == 1
            ? GraphMediaFanoutResultV1.Reconciled : GraphMediaFanoutResultV1.Converged;
        var fanouts = new Dictionary<OperationId, GraphMediaFanoutRecordV1>(_fanouts) { [operation] = row with { Result = result } };
        var residences = new Dictionary<StableId128, GraphMediaControlledResidenceV1>(_residences);
        if (durable == 1)
            foreach (var destination in row.Destinations)
            {
                var residence = residences[destination.Residence.ResidenceId];
                residences[residence.ResidenceId] = residence with { State = GraphMediaResidenceStateV1.Visible };
            }
        return new(result, Next(residences, new(_receipts), fanouts), ownership, []);
    }

    internal GraphMediaFanoutTransitionV1 LoseFanoutOutcome(OperationId operation, Hash256 requestHash,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (!_fanouts.TryGetValue(operation, out var row) || row.RequestHash != requestHash)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        if (FanoutOwnershipState(row, ownership) < 0)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        if (row.Result == GraphMediaFanoutResultV1.OutcomeUnknown)
            return new(GraphMediaFanoutResultV1.OutcomeUnknown, this, ownership, []);
        if (row.Result != GraphMediaFanoutResultV1.Prepared)
            return FanoutFail(GraphMediaFanoutResultV1.WrongState, ownership);
        var fanouts = new Dictionary<OperationId, GraphMediaFanoutRecordV1>(_fanouts)
        { [operation] = row with { Result = GraphMediaFanoutResultV1.OutcomeUnknown } };
        return new(GraphMediaFanoutResultV1.OutcomeUnknown,
            Next(new(_residences), new(_receipts), fanouts), ownership, []);
    }

    private GraphMediaResidenceResultV1 AuthenticateControlled(GraphMediaControlledResidenceRequestV1 request,
        GraphMediaOwnerRecordV1 owner)
    {
        var preGrant = request.Evidence.PreGrantPlan;
        var proof = request.BindingResult.CapacityGrantProof;
        if(!GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(request.FoldBound.Command.PayloadMemory,out var bindingOuter)||bindingOuter is null||!GraphParticipantBindingCodecsV1.TryDecodeBindingCommandBody(bindingOuter.BodyBytes.ToArray(),out var bindingCommand)||bindingCommand is null) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(request.BindingResult.CommandPosition!=request.FoldBound.Command.Position) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(request.BindingResult.FactPosition!=request.FoldBound.Fact.Position) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(!request.BindingResult.ExactCanonicalFactBytes.Span.SequenceEqual(request.FoldBound.Fact.PayloadMemory.Span)) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(request.BindingResult.Binding!=request.FoldBound.Binding) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(request.BindingResult.CapacityGrantProof!=request.FoldBound.CapacityGrantProof) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(preGrant.ReservationCommandPosition!=request.FoldBound.Reservation.Command.Position) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(preGrant.ReservationFactPosition!=request.FoldBound.Reservation.Fact.Position) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(preGrant.OperationId!=bindingCommand.OperationId) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(preGrant.ParticipantId!=request.BindingResult.Binding.ParticipantId) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(preGrant.FactoryKey!=request.BindingResult.Binding.ParticipantFactoryKey) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(!preGrant.OrderedNodeKeys.SequenceEqual(request.BindingResult.Binding.OrderedTopologyNodeKeys)) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(preGrant.GraphGeneration!=owner.Key.GraphGeneration) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(preGrant.Request.Authority.Session!=owner.Key.Session) return GraphMediaResidenceResultV1.StaleGeneration;
        if(proof.GrantId!=request.Evidence.GrantId) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(proof.GrantedAt!=request.Evidence.GrantedAt) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(proof.CurrentFact!=request.Evidence.CurrentFact) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(proof.RequiredChargeCount!=preGrant.Request.Charges.Count) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(proof.RequiredChargeCoverageHash!=request.Evidence.CoverageHashV2) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(request.Evidence.TopologyFingerprint!=request.Evidence.Topology.Fingerprint) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(request.Evidence.ExecutableFingerprint!=request.Evidence.ExecutablePlan.Fingerprint) return GraphMediaResidenceResultV1.AuthorityMismatch;
        if(!request.Evidence.Topology.Nodes.Any(x=>x.Key==request.DestinationNodeKey)||!request.Evidence.ExecutablePlan.NodeBindings.Any(x=>x.NodeKey==request.DestinationNodeKey)||!preGrant.OrderedNodeKeys.Contains(request.DestinationNodeKey)) return GraphMediaResidenceResultV1.AuthorityMismatch;
        return GraphMediaResidenceResultV1.Prepared;
    }

    private GraphMediaResidenceResultV1 SelectAssignment(GraphMediaControlledResidenceRequestV1 request,
        GraphMediaBindingV1 media, out GraphMediaCapacityAssignmentV1? assignment)
    {
        assignment = null;
        ushort dimension; long expectedAmount;
        try { (dimension, expectedAmount) = request.Arm switch { GraphMediaRepresentationArmV1.ResidentBytes => ((ushort)1, media.ByteLength), GraphMediaRepresentationArmV1.ResidentSamples => ((ushort)4, checked(media.FrameCount * media.ChannelCount)), GraphMediaRepresentationArmV1.ResidentTimedBuffer => ((ushort)5, checked(media.EndExclusive - media.Start)), _ => ((ushort)0, 0L) }; }
        catch (OverflowException) { return GraphMediaResidenceResultV1.CapacityMismatch; }
        var preGrant = request.Evidence.PreGrantPlan;
        var candidates = preGrant.Request.Charges.Where(x => x.DimensionId.Value == dimension).ToArray();
        if (candidates.Length != 1) return GraphMediaResidenceResultV1.CapacityMismatch;
        var charge = candidates[0];
        if(!preGrant.Request.Charges.Contains(charge)||!request.Evidence.ExecutablePlan.CapacityCharges.Contains(charge)) return GraphMediaResidenceResultV1.CapacityMismatch;
        if(charge.Scope.Subject is not CapacitySubjectV1.Participant participant||participant.Value!=preGrant.ParticipantId) return GraphMediaResidenceResultV1.CapacityMismatch;
        if(charge.Amount!=expectedAmount) return GraphMediaResidenceResultV1.CapacityMismatch;
        assignment = new(charge, request.Arm); return GraphMediaResidenceResultV1.Prepared;
    }

    private static bool RegistrationEquals(LiveAudioParticipantFactoryRegistrationV1 left,
        LiveAudioParticipantFactoryRegistrationV1 right) =>
        left.FactoryType == right.FactoryType && StringComparer.Ordinal.Equals(left.FactoryIdentity, right.FactoryIdentity) &&
        left.Descriptor == right.Descriptor &&
        left.GraphParticipantAllocationDeclarationBytes.Span.SequenceEqual(right.GraphParticipantAllocationDeclarationBytes.Span) &&
        left.GraphParticipantAllocationDeclarationFingerprint == right.GraphParticipantAllocationDeclarationFingerprint &&
        left.OpaqueResidenceQualification == right.OpaqueResidenceQualification;

    private static bool OpaqueBalance(CapacityChargeBalanceV1 balance, long amount)
    {
        long available; long encumbered;
        try { available = checked(balance.Unactivated + balance.Active); encumbered = checked(balance.EncumberedNormal + balance.EncumberedReserve); }
        catch (OverflowException) { return false; }
        return balance.Charge.Amount == amount && available == amount &&
            balance.Charge.Window is CapacityChargeWindowV1.NoWindow &&
            balance.Released == 0 && balance.Consumed == 0 && balance.AgedOut == 0 && balance.Revoked == 0 &&
            balance.ExplicitlyUnknown == 0 && encumbered == amount;
    }

    internal static Hash256 QuarantineCapacityProofHash(CapacityGrantSnapshotV1 grant) =>
        CapacityProofHash(grant, "hpd-s2-graph-media-quarantine-capacity-proof-v1\0"u8);

    internal static Hash256 OpaqueCapacityProofHash(CapacityGrantSnapshotV1 grant) =>
        CapacityProofHash(grant, "hpd-s2-graph-media-opaque-capacity-proof-v1\0"u8);

    private static Hash256 CapacityProofHash(CapacityGrantSnapshotV1 grant, ReadOnlySpan<byte> domain)
    {
        ArgumentNullException.ThrowIfNull(grant);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(domain);
        static byte[] Fixed(object value)
        {
            var bytes = new byte[16];
            var valid = value switch
            {
                OperationId x => x.TryWriteBytes(bytes),
                CapacityGrantId x => x.TryWriteBytes(bytes),
                CapacityPurposeId x => x.TryWriteBytes(bytes),
                _ => false
            };
            return valid ? bytes : throw new ArgumentException("A valid canonical identity is required.");
        }
        static byte[] I64(long value) { var bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); return bytes; }
        static byte[] Charge(CapacityChargeV1 charge)
        {
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartArray(5); writer.WriteUInt64(charge.DimensionId.Value);
            writer.WriteByteString(CapacityScopeCanonicalCodecV1.Encode(charge.Scope)); writer.WriteInt64(charge.Amount);
            writer.WriteByteString(Fixed(charge.Purpose));
            writer.WriteStartArray(charge.Window is CapacityChargeWindowV1.EndsAt ? 2 : 1);
            writer.WriteUInt64((ushort)charge.Window.Kind);
            if (charge.Window is CapacityChargeWindowV1.EndsAt ends) writer.WriteByteString(MonotonicStampV1Codec.Encode(ends.Value));
            writer.WriteEndArray(); writer.WriteEndArray(); return writer.Encode();
        }
        var authority = new CborWriter(CborConformanceMode.Ctap2Canonical); AuthorityVectorCodecsV1.WriteVector(authority, grant.Authority);
        Field(hash,"grantId"u8,Fixed(grant.GrantId));
        Field(hash,"operationId"u8,Fixed(grant.OperationId));
        Field(hash,"authority"u8,authority.Encode()); Field(hash,"grantedAt"u8,AuthorityPositionCodecsV1.Encode(grant.GrantedAt));
        Field(hash,"currentFact"u8,AuthorityPositionCodecsV1.Encode(grant.CurrentFact)); Field(hash,"state"u8,I64((long)grant.State));
        Field(hash,"expiryKind"u8,I64((long)grant.ExpiresAt.Kind));
        if (grant.ExpiresAt is CapacityGrantExpiryV1.At at) Field(hash,"expiryAt"u8,MonotonicStampV1Codec.Encode(at.Value));
        Field(hash,"balanceCount"u8,I64(grant.Balances.Count));
        for (var i = 0; i < grant.Balances.Count; i++)
        {
            var balance = grant.Balances[i]; Field(hash,"balanceIndex"u8,I64(i)); Field(hash,"charge"u8,Charge(balance.Charge));
            foreach (var value in new[] { balance.NormalAllocation, balance.ReserveAllocation, balance.Unactivated,
                balance.Active, balance.Released, balance.Consumed, balance.AgedOut, balance.Revoked,
                balance.ExplicitlyUnknown, balance.EncumberedNormal, balance.EncumberedReserve })
                Field(hash,"balanceValue"u8,I64(value));
        }
        return Hash256.FromBytes(hash.GetHashAndReset());
    }

    internal static Hash256 OpaqueHash(GraphMediaOpaqueIngressRequestV1 request, GraphMediaOwnerRecordV1 owner,
        LiveAudioOpaqueResidenceQualificationV1 qualification, Hash256 contributionHash, Hash256 capacityProofHash)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(qualification);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s2-graph-media-opaque-ingress-v1\0"u8);
        static byte[] Fixed(object value)
        {
            var bytes = value is Hash256 ? new byte[32] : new byte[16];
            var valid = value switch
            {
                OperationId x => x.TryWriteBytes(bytes), StableId128 x => x.TryWriteBytes(bytes),
                ParticipantId x => x.TryWriteBytes(bytes), ProviderId x => x.TryWriteBytes(bytes),
                LiveSessionId x => x.TryWriteBytes(bytes), RuntimeGenerationId x => x.TryWriteBytes(bytes),
                GraphGenerationId x => x.TryWriteBytes(bytes), CapacityGrantId x => x.TryWriteBytes(bytes),
                Hash256 x => x.TryWriteBytes(bytes), _ => false
            };
            return valid ? bytes : throw new ArgumentException("A valid canonical identity is required.");
        }
        static byte[] U64(ulong value) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes,value); return bytes; }
        static byte[] I64(long value) { var bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes,value); return bytes; }
        static byte[] U16(ushort value) { var bytes = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes,value); return bytes; }
        static byte[] OwnerKey(GraphMediaOwnerKeyV1 key)
        {
            var writer=new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(4);
            writer.WriteByteString(Fixed(key.Session.LiveSessionId)); writer.WriteByteString(Fixed(key.GraphGeneration));
            writer.WriteByteString(Fixed(key.Session.RuntimeGenerationId)); writer.WriteByteString(Fixed(key.MediaId)); writer.WriteEndArray(); return writer.Encode();
        }
        Field(hash,"operationId"u8,Fixed(request.OperationId)); Field(hash,"residenceId"u8,Fixed(request.ResidenceId));
        Field(hash,"sourceResidenceId"u8,Fixed(request.SourceResidenceId)); Field(hash,"ownerId"u8,Fixed(request.OwnerId));
        Field(hash,"ownerKey"u8,OwnerKey(owner.Key)); Field(hash,"authorityRequest"u8,Fixed(request.AuthorityEvidence.RequestHash));
        Field(hash,"bindingCommand"u8,AuthorityPositionCodecsV1.Encode(request.AuthorityEvidence.BindingResult.CommandPosition));
        Field(hash,"bindingFact"u8,AuthorityPositionCodecsV1.Encode(request.AuthorityEvidence.BindingResult.FactPosition));
        Field(hash,"reservationCommand"u8,AuthorityPositionCodecsV1.Encode(request.AuthorityEvidence.Evidence.PreGrantPlan.ReservationCommandPosition));
        Field(hash,"reservationFact"u8,AuthorityPositionCodecsV1.Encode(request.AuthorityEvidence.Evidence.PreGrantPlan.ReservationFactPosition));
        Field(hash,"participantId"u8,Fixed(request.AuthorityEvidence.BindingResult.Binding.ParticipantId));
        Field(hash,"factoryKey"u8,Encoding.ASCII.GetBytes(request.SelectedRegistration.Descriptor.FactoryKey.ToString()));
        Field(hash,"participantCatalog"u8,Fixed(request.ParticipantCatalog.Fingerprint)); Field(hash,"providerCatalog"u8,Fixed(request.ProviderCatalog.Fingerprint));
        Field(hash,"providerContribution"u8,Fixed(contributionHash)); Field(hash,"providerId"u8,Fixed(qualification.ProviderId));
        Field(hash,"externalReference"u8,Fixed(request.ExternalReferenceFingerprint)); Field(hash,"submittedOperations"u8,U16(request.SubmittedOperations));
        Field(hash,"submittedBytes"u8,U64(request.SubmittedBytes)); Field(hash,"maximumAge"u8,I64(request.MaximumAge.Nanoseconds));
        Field(hash,"admittedAt"u8,MonotonicStampV1Codec.Encode(request.AdmittedAt)); Field(hash,"control"u8,[(byte)qualification.Control]);
        Field(hash,"grantId"u8,Fixed(request.Grant.GrantId)); Field(hash,"grantedAt"u8,AuthorityPositionCodecsV1.Encode(request.Grant.GrantedAt));
        Field(hash,"currentFact"u8,AuthorityPositionCodecsV1.Encode(request.Grant.CurrentFact)); Field(hash,"capacityProof"u8,Fixed(capacityProofHash));
        return Hash256.FromBytes(hash.GetHashAndReset());
    }

    internal static Hash256 QuarantineHash(GraphMediaQuarantineIngressRequestV1 request,
        GraphMediaOwnerRecordV1 owner, CapacityChargeV1 charge, Hash256 capacityProofHash)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(owner);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s2-graph-media-quarantine-ingress-v1\0"u8);
        byte[] Bytes(object value)
        {
            if (value is GraphMediaOwnerKeyV1 key) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(4); writer.WriteByteString(Bytes(key.Session.LiveSessionId)); writer.WriteByteString(Bytes(key.GraphGeneration)); writer.WriteByteString(Bytes(key.Session.RuntimeGenerationId)); writer.WriteByteString(Bytes(key.MediaId)); writer.WriteEndArray(); return writer.Encode(); }
            if (value is GraphMediaBindingV1 media) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(13); writer.WriteInt64(media.Start); writer.WriteInt64(media.EndExclusive); writer.WriteByteString(Bytes(media.FormatId)); writer.WriteUInt64(media.FormatRevision); writer.WriteUInt64(media.SampleRateHz); writer.WriteUInt64(media.ChannelCount); writer.WriteUInt64(media.BytesPerSample); writer.WriteByteString(Bytes(media.ClockId)); writer.WriteUInt64(media.ClockRevision); writer.WriteUInt64(media.Sequence); writer.WriteUInt64((byte)media.Discontinuity); writer.WriteInt64(media.ByteLength); writer.WriteInt64(media.FrameCount); writer.WriteEndArray(); return writer.Encode(); }
            if (value is CapacityChargeV1 capacity) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(5); writer.WriteUInt64(capacity.DimensionId.Value); writer.WriteByteString(CapacityScopeCanonicalCodecV1.Encode(capacity.Scope)); writer.WriteInt64(capacity.Amount); writer.WriteByteString(Bytes(capacity.Purpose)); writer.WriteStartArray(capacity.Window is CapacityChargeWindowV1.EndsAt ? 2 : 1); writer.WriteUInt64((ushort)capacity.Window.Kind); if (capacity.Window is CapacityChargeWindowV1.EndsAt ends) writer.WriteByteString(MonotonicStampV1Codec.Encode(ends.Value)); writer.WriteEndArray(); writer.WriteEndArray(); return writer.Encode(); }
            var result = value switch { OperationId => new byte[16], StableId128 => new byte[16], LiveSessionId => new byte[16], RuntimeGenerationId => new byte[16], GraphGenerationId => new byte[16], SchemaId => new byte[16], CapacityGrantId => new byte[16], CapacityPurposeId => new byte[16], Hash256 => new byte[32], _ => throw new ArgumentException("Unsupported canonical value.") };
            var written = value switch { OperationId x => x.TryWriteBytes(result), StableId128 x => x.TryWriteBytes(result), LiveSessionId x => x.TryWriteBytes(result), RuntimeGenerationId x => x.TryWriteBytes(result), GraphGenerationId x => x.TryWriteBytes(result), SchemaId x => x.TryWriteBytes(result), CapacityGrantId x => x.TryWriteBytes(result), CapacityPurposeId x => x.TryWriteBytes(result), Hash256 x => x.TryWriteBytes(result), _ => false };
            return written ? result : throw new ArgumentException("Invalid canonical value.");
        }
        Field(hash,"operationId"u8,Bytes(request.OperationId)); Field(hash,"residenceId"u8,Bytes(request.ResidenceId));
        Field(hash,"sourceResidenceId"u8,Bytes(request.SourceResidenceId)); Field(hash,"ownerId"u8,Bytes(request.OwnerId));
        Field(hash,"ownerKey"u8,Bytes(owner.Key)); Field(hash,"mediaBinding"u8,Bytes(owner.Media)); Field(hash,"schemaId"u8,Bytes(request.SchemaId));
        Field(hash,"grantId"u8,Bytes(request.Grant.GrantId)); Field(hash,"grantedAt"u8,AuthorityPositionCodecsV1.Encode(request.Grant.GrantedAt));
        Field(hash,"currentFact"u8,AuthorityPositionCodecsV1.Encode(request.Grant.CurrentFact)); Field(hash,"capacityProofHash"u8,Bytes(capacityProofHash));
        Field(hash,"capacityCharge"u8,Bytes(charge)); return Hash256.FromBytes(hash.GetHashAndReset());
    }

    internal static Hash256 ResidenceHash(GraphMediaControlledResidenceRequestV1 request,
        GraphMediaOwnerRecordV1 owner, GraphMediaCapacityAssignmentV1 assignment)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s2-graph-media-controlled-residence-v1\0"u8);
        byte[] Bytes(object value)
        {
            if (value is BoundedAscii ascii) return Encoding.ASCII.GetBytes(ascii.ToString());
            if (value is JournalPositionV1 position) return AuthorityPositionCodecsV1.Encode(position);
            if (value is byte arm) return [arm];
            if (value is Enum enumeration) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, Convert.ToInt32(enumeration)); return bytes; }
            if (value is GraphMediaOwnerKeyV1 key) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(4); writer.WriteByteString(Bytes(key.Session.LiveSessionId)); writer.WriteByteString(Bytes(key.GraphGeneration)); writer.WriteByteString(Bytes(key.Session.RuntimeGenerationId)); writer.WriteByteString(Bytes(key.MediaId)); writer.WriteEndArray(); return writer.Encode(); }
            if (value is GraphMediaBindingV1 media) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(13); writer.WriteInt64(media.Start); writer.WriteInt64(media.EndExclusive); writer.WriteByteString(Bytes(media.FormatId)); writer.WriteUInt64(media.FormatRevision); writer.WriteUInt64(media.SampleRateHz); writer.WriteUInt64(media.ChannelCount); writer.WriteUInt64(media.BytesPerSample); writer.WriteByteString(Bytes(media.ClockId)); writer.WriteUInt64(media.ClockRevision); writer.WriteUInt64(media.Sequence); writer.WriteUInt64((byte)media.Discontinuity); writer.WriteInt64(media.ByteLength); writer.WriteInt64(media.FrameCount); writer.WriteEndArray(); return writer.Encode(); }
            if (value is CapacityChargeV1 charge) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(5); writer.WriteUInt64(charge.DimensionId.Value); writer.WriteByteString(CapacityScopeCanonicalCodecV1.Encode(charge.Scope)); writer.WriteInt64(charge.Amount); writer.WriteByteString(Bytes(charge.Purpose)); writer.WriteStartArray(charge.Window is CapacityChargeWindowV1.EndsAt ? 2 : 1); writer.WriteUInt64((ushort)charge.Window.Kind); if (charge.Window is CapacityChargeWindowV1.EndsAt endsAt) writer.WriteByteString(MonotonicStampV1Codec.Encode(endsAt.Value)); writer.WriteEndArray(); writer.WriteEndArray(); return writer.Encode(); }
            var result = value switch { OperationId x => new byte[16], StableId128 x => new byte[16], SessionId x => new byte[16], LiveSessionId x => new byte[16], RuntimeGenerationId x => new byte[16], GraphGenerationId x => new byte[16], ParticipantId x => new byte[16], CapacityGrantId x => new byte[16], CapacityPurposeId x => new byte[16], Hash256 x => new byte[32], _ => throw new ArgumentException("Unsupported canonical value.") };
            var written = value switch { OperationId x => x.TryWriteBytes(result), StableId128 x => x.TryWriteBytes(result), SessionId x => x.TryWriteBytes(result), LiveSessionId x => x.TryWriteBytes(result), RuntimeGenerationId x => x.TryWriteBytes(result), GraphGenerationId x => x.TryWriteBytes(result), ParticipantId x => x.TryWriteBytes(result), CapacityGrantId x => x.TryWriteBytes(result), CapacityPurposeId x => x.TryWriteBytes(result), Hash256 x => x.TryWriteBytes(result), _ => false };
            return written ? result : throw new ArgumentException("Invalid canonical value.");
        }
        Field(hash,"operationId"u8,Bytes(request.OperationId)); Field(hash,"residenceId"u8,Bytes(request.ResidenceId));
        Field(hash,"sourceOwnerId"u8,Bytes(request.SourceOwnerId)); Field(hash,"destinationOwnerId"u8,Bytes(request.DestinationOwnerId));
        Field(hash,"ownerKey"u8,Bytes(owner.Key)); Field(hash,"mediaBinding"u8,Bytes(owner.Media));
        Field(hash,"destinationNodeKey"u8,Bytes(request.DestinationNodeKey)); Field(hash,"representationArm"u8,Bytes(request.Arm));
        Field(hash,"capacityCharge"u8,Bytes(assignment.Charge)); Field(hash,"bindingCommandPosition"u8,Bytes(request.BindingResult.CommandPosition));
        Field(hash,"bindingFactPosition"u8,Bytes(request.BindingResult.FactPosition)); Field(hash,"reservationCommandPosition"u8,Bytes(request.Evidence.PreGrantPlan.ReservationCommandPosition));
        Field(hash,"reservationFactPosition"u8,Bytes(request.Evidence.PreGrantPlan.ReservationFactPosition)); Field(hash,"participantId"u8,Bytes(request.BindingResult.Binding.ParticipantId));
        Field(hash,"grantId"u8,Bytes(request.Evidence.GrantId)); Field(hash,"grantedAt"u8,Bytes(request.Evidence.GrantedAt));
        Field(hash,"currentFact"u8,Bytes(request.Evidence.CurrentFact)); Field(hash,"coverageHashV2"u8,Bytes(request.Evidence.CoverageHashV2));
        Field(hash,"topologyFingerprint"u8,Bytes(request.Evidence.TopologyFingerprint)); Field(hash,"executableFingerprint"u8,Bytes(request.Evidence.ExecutableFingerprint));
        return Hash256.FromBytes(hash.GetHashAndReset());
    }

    internal static Hash256 FanoutHash(GraphMediaFanoutRequestV1 request, GraphMediaOwnerRecordV1 source)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-s2-graph-media-fanout-v1\0"u8);
        byte[] Bytes(object value)
        {
            if (value is BoundedAscii ascii) return Encoding.ASCII.GetBytes(ascii.ToString());
            if (value is Enum enumeration) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, Convert.ToInt32(enumeration)); return bytes; }
            if (value is GraphMediaOwnerKeyV1 key) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(4); writer.WriteByteString(Bytes(key.Session.LiveSessionId)); writer.WriteByteString(Bytes(key.GraphGeneration)); writer.WriteByteString(Bytes(key.Session.RuntimeGenerationId)); writer.WriteByteString(Bytes(key.MediaId)); writer.WriteEndArray(); return writer.Encode(); }
            if (value is GraphMediaBindingV1 media) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(13); writer.WriteInt64(media.Start); writer.WriteInt64(media.EndExclusive); writer.WriteByteString(Bytes(media.FormatId)); writer.WriteUInt64(media.FormatRevision); writer.WriteUInt64(media.SampleRateHz); writer.WriteUInt64(media.ChannelCount); writer.WriteUInt64(media.BytesPerSample); writer.WriteByteString(Bytes(media.ClockId)); writer.WriteUInt64(media.ClockRevision); writer.WriteUInt64(media.Sequence); writer.WriteUInt64((byte)media.Discontinuity); writer.WriteInt64(media.ByteLength); writer.WriteInt64(media.FrameCount); writer.WriteEndArray(); return writer.Encode(); }
            var result = value switch { OperationId x => new byte[16], StableId128 x => new byte[16], SessionId x => new byte[16], LiveSessionId x => new byte[16], RuntimeGenerationId x => new byte[16], GraphGenerationId x => new byte[16], Hash256 x => new byte[32], _ => throw new ArgumentException("Unsupported canonical value.") };
            var written = value switch { OperationId x => x.TryWriteBytes(result), StableId128 x => x.TryWriteBytes(result), SessionId x => x.TryWriteBytes(result), LiveSessionId x => x.TryWriteBytes(result), RuntimeGenerationId x => x.TryWriteBytes(result), GraphGenerationId x => x.TryWriteBytes(result), Hash256 x => x.TryWriteBytes(result), _ => false };
            return written ? result : throw new ArgumentException("Invalid canonical value.");
        }
        byte[] ListBytes<T>(IEnumerable<T> values, Func<T, byte[]> encode) { var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); var rows = values.ToArray(); writer.WriteStartArray(rows.Length); foreach (var row in rows) writer.WriteByteString(encode(row)); writer.WriteEndArray(); return writer.Encode(); }
        Field(hash,"operationId"u8,Bytes(request.OperationId)); Field(hash,"sourceOwnerId"u8,Bytes(request.SourceOwnerId));
        Field(hash,"ownerKey"u8,Bytes(source.Key)); Field(hash,"mediaBinding"u8,Bytes(source.Media)); Field(hash,"mode"u8,Bytes(request.Mode));
        Field(hash,"orderedDestinationOwnerIds"u8,ListBytes(request.Destinations,x=>Bytes(x.DestinationOwnerId)));
        Field(hash,"orderedDestinationNodeKeys"u8,ListBytes(request.Destinations,x=>Bytes(x.DestinationNodeKey)));
        Field(hash,"orderedResidenceOperationIds"u8,ListBytes(request.Destinations,x=>Bytes(x.Residence.OperationId)));
        Field(hash,"orderedResidenceRequestHashes"u8,ListBytes(request.Destinations,x=>Bytes(x.Residence.RequestHash)));
        return Hash256.FromBytes(hash.GetHashAndReset());
    }

    private static void Field(IncrementalHash hash, ReadOnlySpan<byte> label, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length,label.Length); hash.AppendData(length); hash.AppendData(label);
        BinaryPrimitives.WriteInt32BigEndian(length,value.Length); hash.AppendData(length); hash.AppendData(value);
    }
    private int FanoutOwnershipState(GraphMediaFanoutRecordV1 row, GraphMediaOwnershipLedgerV1 ownership)
    {
        if (ownership.Session != Session || ownership.GraphGeneration != GraphGeneration ||
            row.Destinations.Count == 0 ||
            !_residences.TryGetValue(row.Destinations[0].Residence.ResidenceId, out var firstResidence) ||
            !ownership.Owners.TryGetValue(row.SourceOwnerId, out var source) ||
            source.Key != firstResidence.OwnerKey || source.Media != firstResidence.Media)
            return -1;
        var expectedSourceState = row.Mode == GraphMediaFanoutModeV1.Copy
            ? GraphMediaOwnerStateV1.Owned : GraphMediaOwnerStateV1.Transferred;
        var durable = 0;
        foreach (var destination in row.Destinations)
        {
            if (!ownership.Owners.TryGetValue(destination.DestinationOwnerId, out var owner)) continue;
            if (owner.State != GraphMediaOwnerStateV1.Owned ||
                !_residences.TryGetValue(destination.Residence.ResidenceId, out var residence) ||
                owner.Key != residence.OwnerKey || owner.Media != residence.Media)
                return -1;
            durable++;
        }
        if (durable != 0 && durable != row.Destinations.Count) return -1;
        var expectedResidenceState = row.Result is GraphMediaFanoutResultV1.Committed or GraphMediaFanoutResultV1.Reconciled
            ? GraphMediaResidenceStateV1.Visible : GraphMediaResidenceStateV1.Prepared;
        if (row.Destinations.Any(x => !_residences.TryGetValue(x.Residence.ResidenceId, out var residence) ||
            residence.State != expectedResidenceState)) return -1;
        if (durable == 0) return source.State == GraphMediaOwnerStateV1.Owned ? 0 : -1;
        return source.State == expectedSourceState ? 1 : -1;
    }
    private GraphMediaResidenceTransitionV1 ResidenceFail(GraphMediaResidenceResultV1 result) => new(result, this);
    private GraphMediaFanoutTransitionV1 FanoutFail(GraphMediaFanoutResultV1 result, GraphMediaOwnershipLedgerV1 ownership) => new(result, this, ownership, []);
    private GraphMediaResidenceLedgerV1 Next(Dictionary<StableId128, GraphMediaControlledResidenceV1> residences,
        Dictionary<OperationId, GraphMediaResidenceReceiptV1> receipts,
        Dictionary<OperationId, GraphMediaFanoutRecordV1> fanouts) =>
        new(Session, GraphGeneration, residences, new(_quarantines), new(_opaques), receipts, fanouts);
    private GraphMediaResidenceLedgerV1 Next(Dictionary<StableId128, GraphMediaControlledResidenceV1> residences,
        Dictionary<StableId128, GraphMediaQuarantineResidenceV1> quarantines,
        Dictionary<OperationId, GraphMediaResidenceReceiptV1> receipts,
        Dictionary<OperationId, GraphMediaFanoutRecordV1> fanouts) =>
        new(Session, GraphGeneration, residences, quarantines, new(_opaques), receipts, fanouts);
    private GraphMediaResidenceLedgerV1 Next(Dictionary<StableId128, GraphMediaControlledResidenceV1> residences,
        Dictionary<StableId128, GraphMediaQuarantineResidenceV1> quarantines,
        Dictionary<StableId128, GraphMediaOpaqueResidenceV1> opaques,
        Dictionary<OperationId, GraphMediaResidenceReceiptV1> receipts,
        Dictionary<OperationId, GraphMediaFanoutRecordV1> fanouts) =>
        new(Session, GraphGeneration, residences, quarantines, opaques, receipts, fanouts);
}

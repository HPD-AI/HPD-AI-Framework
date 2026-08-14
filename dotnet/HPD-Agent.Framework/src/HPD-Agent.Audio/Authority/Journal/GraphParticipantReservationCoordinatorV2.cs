using System.Formats.Cbor;
using HPD.Agent.Audio;

namespace HPD.Agent.Authority;

internal sealed record GraphParticipantReservationRequestV2
{
    private readonly byte[] _bytes;
    internal GraphParticipantReservationRequestV2(ReadOnlyMemory<byte> exactCanonicalCommandBytes, LiveAudioParticipantPlanV1 participantPlan, LiveAudioParticipantCatalogManifestV1 catalog, LiveAudioParticipantFactoryRegistrationV1 selectedRegistration, CorrelationEnvelopeV1 correlation, UtcInstant observedAt, ulong maximumSessionRecords, ulong maximumSessionCanonicalBytes)
    {
        if (exactCanonicalCommandBytes.IsEmpty || exactCanonicalCommandBytes.Length > 65_833) throw new ArgumentOutOfRangeException(nameof(exactCanonicalCommandBytes));
        ParticipantPlan=participantPlan??throw new ArgumentNullException(nameof(participantPlan));Catalog=catalog??throw new ArgumentNullException(nameof(catalog));SelectedRegistration=selectedRegistration??throw new ArgumentNullException(nameof(selectedRegistration));
        if (!GraphParticipantReservationCodecsV2.TryDecodeReservationCommand(exactCanonicalCommandBytes, out var outer) || outer is null || !GraphParticipantReservationCodecsV2.TryDecodeReservationCommandBody(outer.BodyBytes.ToArray(), out var body) || body is null || !correlation.IsValid || correlation.OperationId != body.OperationId || correlation.ParticipantId is not null || !ValidateTrustedRequest(outer,body,ParticipantPlan,Catalog,SelectedRegistration)) throw new ArgumentException("Invalid command proposal metadata.");
        if (maximumSessionRecords is 0 or > 65_536) throw new ArgumentOutOfRangeException(nameof(maximumSessionRecords));
        if (maximumSessionCanonicalBytes is 0 or > 536_870_912) throw new ArgumentOutOfRangeException(nameof(maximumSessionCanonicalBytes));
        _bytes = exactCanonicalCommandBytes.ToArray(); ExactCanonicalCommandBytes = _bytes; Correlation = correlation; ObservedAt = observedAt;
        MaximumSessionRecords = maximumSessionRecords; MaximumSessionCanonicalBytes = maximumSessionCanonicalBytes;
    }
    internal ReadOnlyMemory<byte> ExactCanonicalCommandBytes { get; }
    internal LiveAudioParticipantPlanV1 ParticipantPlan { get; }
    internal LiveAudioParticipantCatalogManifestV1 Catalog { get; }
    internal LiveAudioParticipantFactoryRegistrationV1 SelectedRegistration { get; }
    internal CorrelationEnvelopeV1 Correlation { get; }
    internal UtcInstant ObservedAt { get; }
    internal ulong MaximumSessionRecords { get; }
    internal ulong MaximumSessionCanonicalBytes { get; }

    internal static bool ValidateTrustedRequest(GraphParticipantReservationCommandV2 outer,GraphParticipantReservationCommandBodyV2 body,LiveAudioParticipantPlanV1 participantPlan,LiveAudioParticipantCatalogManifestV1 catalog,LiveAudioParticipantFactoryRegistrationV1 selectedRegistration)
    {
        var ParticipantPlan=participantPlan;var Catalog=catalog;var SelectedRegistration=selectedRegistration;
        var graphs=outer.ExpectedAuthority.Axes.Where(x=>x.AxisId==AuthorityAxisId.Graph&&x.Value is AuthorityAxisValueV1.Graph).Select(x=>((AuthorityAxisValueV1.Graph)x.Value).Value).ToArray();
        if(body.ParticipantPlanFingerprint!=ParticipantPlan.Fingerprint||body.RuntimeGeneration!=outer.Session.RuntimeGenerationId||graphs.Length!=1||graphs[0]!=body.GraphGeneration||!ParticipantPlan.Descriptors.Contains(SelectedRegistration.Descriptor)||!Catalog.TryGet(body.ParticipantFactoryKey,out var found)||!ReferenceEquals(found,SelectedRegistration)||SelectedRegistration.Descriptor.FactoryKey!=body.ParticipantFactoryKey||SelectedRegistration.GraphParticipantAllocationDeclarationFingerprint is not Hash256 fingerprint||fingerprint!=body.AllocationCarrierFingerprint)return false;
        var carrier=SelectedRegistration.GraphParticipantAllocationDeclarationBytes.ToArray();
        if(!LiveAudioParticipantCatalogManifestV1.TryValidateGraphParticipantAllocationDeclaration(carrier,SelectedRegistration.Descriptor.FactoryKey,SelectedRegistration.Descriptor.CapacityDimensions,fingerprint)||!TryCarrierNodes(carrier,out var factory,out var nodes)||!StringComparer.Ordinal.Equals(factory,body.ParticipantFactoryKey.ToString()))return false;
        var ordered=nodes.OrderBy(static x=>x,StringComparer.Ordinal).ToArray();
        return ordered.Distinct(StringComparer.Ordinal).Count()==ordered.Length&&ordered.SequenceEqual(body.OrderedTopologyNodeKeys.Select(static x=>x.ToString()),StringComparer.Ordinal);
    }

    private static bool TryCarrierNodes(byte[] carrier,out string factory,out string[] nodes)
    {
        factory="";nodes=[];try{var r=new CborReader(carrier,CborConformanceMode.Ctap2Canonical);if(r.ReadStartMap()!=4||r.ReadUInt64()!=0||r.ReadUInt64()!=1||r.ReadUInt64()!=1)return false;factory=r.ReadTextString();if(r.ReadUInt64()!=2)return false;var count=r.ReadStartArray();if(count is null or <1 or >64)return false;nodes=new string[count.Value];for(var i=0;i<nodes.Length;i++)nodes[i]=r.ReadTextString();return true;}catch{return false;}
    }
}

internal abstract record GraphParticipantReservationResultV2
{
    private GraphParticipantReservationResultV2() { }
    internal sealed record Applied : GraphParticipantReservationResultV2
    {
        private readonly byte[] _bytes;
        internal Applied(JournalPositionV1 commandPosition, JournalPositionV1 factPosition, ParticipantId participantId, GlobalParticipantAuthorityHeadV1 allocatorHead, ReadOnlyMemory<byte> exactCanonicalFactBytes)
        { CommandPosition = commandPosition; FactPosition = factPosition; ParticipantId = participantId; AllocatorHead = allocatorHead; _bytes = exactCanonicalFactBytes.ToArray(); ExactCanonicalFactBytes = _bytes; }
        internal JournalPositionV1 CommandPosition { get; }
        internal JournalPositionV1 FactPosition { get; }
        internal ParticipantId ParticipantId { get; }
        internal GlobalParticipantAuthorityHeadV1 AllocatorHead { get; }
        internal ReadOnlyMemory<byte> ExactCanonicalFactBytes { get; }
    }
    internal sealed record Rejected : GraphParticipantReservationResultV2
    {
        private readonly byte[] _bytes;
        internal Rejected(JournalPositionV1 commandPosition, JournalPositionV1 factPosition, BoundedAscii safeCode, ReadOnlyMemory<byte> exactCanonicalFactBytes)
        { CommandPosition = commandPosition; FactPosition = factPosition; SafeCode = safeCode; _bytes = exactCanonicalFactBytes.ToArray(); ExactCanonicalFactBytes = _bytes; }
        internal JournalPositionV1 CommandPosition { get; }
        internal JournalPositionV1 FactPosition { get; }
        internal BoundedAscii SafeCode { get; }
        internal ReadOnlyMemory<byte> ExactCanonicalFactBytes { get; }
    }
    internal sealed record RetryRequired(BoundedAscii SafeCode) : GraphParticipantReservationResultV2;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GraphParticipantReservationResultV2;
    internal sealed record OutcomeUnknown(JournalFactId FactId, BoundedAscii SafeCode) : GraphParticipantReservationResultV2;
    internal sealed record RealmFenced(ulong CurrentFenceEpoch) : GraphParticipantReservationResultV2;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GraphParticipantReservationResultV2;
}

internal sealed class GraphParticipantReservationCoordinatorV2
{
    private readonly IAuthorityJournalV1 _sessionJournal;
    private readonly IGlobalParticipantAllocatorDurableClaimPortV1 _allocatorClaims;
    private readonly IGlobalParticipantAllocatorReconciliationPortV1 _allocatorReconciliation;
    private readonly IGlobalParticipantAllocatorDurableSnapshotPortV1 _allocatorSnapshots;
    private readonly GlobalParticipantAllocatorRealmLeaseV1 _allocatorLease;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    internal GraphParticipantReservationCoordinatorV2(IAuthorityJournalV1 sessionJournal, IGlobalParticipantAllocatorDurableClaimPortV1 allocatorClaims, IGlobalParticipantAllocatorReconciliationPortV1 allocatorReconciliation, IGlobalParticipantAllocatorDurableSnapshotPortV1 allocatorSnapshots, GlobalParticipantAllocatorRealmLeaseV1 allocatorLease)
    { _sessionJournal = sessionJournal ?? throw new ArgumentNullException(nameof(sessionJournal)); _allocatorClaims = allocatorClaims ?? throw new ArgumentNullException(nameof(allocatorClaims)); _allocatorReconciliation = allocatorReconciliation ?? throw new ArgumentNullException(nameof(allocatorReconciliation)); _allocatorSnapshots = allocatorSnapshots ?? throw new ArgumentNullException(nameof(allocatorSnapshots)); _allocatorLease = allocatorLease ?? throw new ArgumentNullException(nameof(allocatorLease)); }

    internal async ValueTask<GraphParticipantReservationResultV2> ReserveAsync(GraphParticipantReservationRequestV2 request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = request.ExactCanonicalCommandBytes.ToArray();
            if (!GraphParticipantReservationCodecsV2.TryDecodeReservationCommand(bytes, out var outer) || outer is null || !GraphParticipantReservationCodecsV2.TryDecodeReservationCommandBody(outer.BodyBytes.ToArray(), out var body) || body is null)
                return new GraphParticipantReservationResultV2.Quarantined(new("invalid-payload"));
            if(!GraphParticipantReservationRequestV2.ValidateTrustedRequest(outer,body,request.ParticipantPlan,request.Catalog,request.SelectedRegistration))return Q("request-authority-invalid");
            var history = await ReadAsync(outer.Session, request, cancellationToken).ConfigureAwait(false); if (history.Result is not null) return history.Result;
            var query = history.Fold!.Query(body.OperationId);
            if (query is GraphParticipantReservationFoldV2.AppliedReservation applied) return await ExistingAppliedAsync(applied, cancellationToken).ConfigureAwait(false);
            if (query is GraphParticipantReservationFoldV2.RejectedReservation rejected) return Existing(rejected);
            AuthorityFactEnvelopeV1? command;
            if (query is GraphParticipantReservationFoldV2.CommandOnly commandOnly)
            { if (!commandOnly.Command.PayloadBytes.SequenceEqual(bytes)) return Q("contradictory-duplicate"); command = commandOnly.Command; }
            else
            {
                var registration = GraphParticipantReservationPayloadRegistrationsV2.ReservationCommand;
                var factId = GraphParticipantReservationFactIdsV2.ReservationCommand(outer.Session, body.OperationId);
                var proposal = new ProposedAuthorityFactV1(factId, null, OwnerSliceId.S1, registration.Schema, bytes, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, bytes), request.Correlation, request.ObservedAt);
                if ((ulong)bytes.Length + 4425UL > 8192UL || history.Count >= request.MaximumSessionRecords || history.Bytes > (request.MaximumSessionCanonicalBytes - (ulong)Math.Min(request.MaximumSessionCanonicalBytes, (ulong)bytes.Length + 4425UL))) return S("journal-capacity-refused");
                var appended = await AppendCommandAsync(outer.Session, body.OperationId, proposal, request, history.Through, cancellationToken).ConfigureAwait(false);
                if (appended.Result is not null) return appended.Result;
                command = appended.Envelope!;
            }

            var electedHistory=await ReadAsync(outer.Session,request,cancellationToken).ConfigureAwait(false);if(electedHistory.Result is not null)return electedHistory.Result;
            var electedQuery=electedHistory.Fold!.Query(body.OperationId);if(electedQuery is GraphParticipantReservationFoldV2.AppliedReservation electedApplied)return await ExistingAppliedAsync(electedApplied,cancellationToken).ConfigureAwait(false);if(electedQuery is GraphParticipantReservationFoldV2.RejectedReservation electedRejected)return Existing(electedRejected);if(electedQuery is not GraphParticipantReservationFoldV2.CommandOnly exactCommand||!exactCommand.Command.PayloadBytes.SequenceEqual(bytes))return Q("contradictory-duplicate");command=exactCommand.Command;
            var election=electedHistory.Fold.Elect(body.OperationId);
            if(election is GraphParticipantReservationFoldV2.Follower)return await RejectWithoutAllocatorAsync(command,body,new("participant-already-reserved"),body.ExpectedReservationFact,request,cancellationToken).ConfigureAwait(false);
            if(election is GraphParticipantReservationFoldV2.BlockedByApplied blocked)return await RejectWithoutAllocatorAsync(command,body,new("participant-already-reserved"),blocked.AppliedFact,request,cancellationToken).ConfigureAwait(false);
            if(election is GraphParticipantReservationFoldV2.PredecessorConflict conflict)return await RejectWithoutAllocatorAsync(command,body,new("reservation-predecessor-conflict"),conflict.ActualPredecessor,request,cancellationToken).ConfigureAwait(false);
            if(election is not GraphParticipantReservationFoldV2.Leader)return Q("session-history-invalid");

            for (var attempt = 0; attempt < 3; attempt++)
            {
                GlobalParticipantAllocatorDurableSnapshotResultV1 snapshotResult;
                try { snapshotResult = await _allocatorSnapshots.ReadAsync(new(_allocatorLease), cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { return S("allocator-store-unavailable"); }
                if (snapshotResult is GlobalParticipantAllocatorDurableSnapshotResultV1.RealmFenced fenced) return new GraphParticipantReservationResultV2.RealmFenced(fenced.CurrentFenceEpoch);
                if (snapshotResult is GlobalParticipantAllocatorDurableSnapshotResultV1.StoreUnavailable) return S("allocator-store-unavailable");
                if (snapshotResult is GlobalParticipantAllocatorDurableSnapshotResultV1.Quarantined) return Q("allocator-quarantined");
                var snapshot = ((GlobalParticipantAllocatorDurableSnapshotResultV1.Current)snapshotResult).Snapshot;
                var allocatorFold = GlobalParticipantAllocatorFoldV1.Create(snapshot.JournalId);
                foreach (var record in snapshot.ExactCanonicalRecords) if (allocatorFold.Apply(record) is GlobalParticipantAllocatorFoldApplyResultV1.InvalidHistory) return Q("allocator-snapshot-invalid");
                if (allocatorFold.Complete() is not GlobalParticipantAllocatorFoldResultV1.Current current || current.Snapshot.Head != snapshot.Head || current.Snapshot.RecordCount != snapshot.RecordCount || current.Snapshot.TotalCanonicalRecordBytes != snapshot.TotalCanonicalRecordBytes) return Q("allocator-snapshot-invalid");
                var source = new GlobalParticipantAllocationSourceV1(outer.Session.LiveSessionId, command.Position, command.PayloadHash, Hash256.Compute(outer.BodyBytes));
                var fingerprint = GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(source); var participant = GlobalParticipantAllocatorFactIdsV1.Participant(outer.Session.LiveSessionId, body.OperationId, fingerprint); var proof = current.Snapshot.Query(participant);
                var outcome = proof.Owner is null ? new ParticipantIdClaimOutcomeV1(1, null, null) : new ParticipantIdClaimOutcomeV1(2, proof.Owner.ClaimHead.Position, new("participant-id-owned"));
                var assigned = new GlobalParticipantAuthorityPositionV1(snapshot.JournalId, snapshot.RecordCount + 1);
                var claimBody = new GlobalParticipantClaimRecordBodyV1(body.OperationId, source, participant, snapshot.Head, proof, outcome, assigned, body.ObservedAt);
                var claimBodyBytes = GlobalParticipantAllocatorCodecsV1.Encode(claimBody); var claimOuterBytes = GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(outer.Session, outer.ExpectedAuthority, claimBodyBytes)); var claimFact = GlobalParticipantAllocatorFactIdsV1.Fact(outer.Session.LiveSessionId, body.OperationId, fingerprint);
                var claimRequest = new GlobalParticipantAllocatorDurableClaimRequestV1(_allocatorLease, snapshot.Head, claimOuterBytes, claimFact);
                var refreshHead = false;
                for (var candidateAttempt = 0; candidateAttempt < 3; candidateAttempt++)
                {
                    GlobalParticipantAllocatorDurableClaimResultV1 claim;
                    var claimThrew = false;
                    try
                    {
                        claim = await _allocatorClaims.ClaimAsync(claimRequest, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        claimThrew = true;
                        claim = new GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown(claimFact, new("operation-interrupted"));
                    }
                    catch (IOException)
                    {
                        claimThrew = true;
                        claim = new GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown(claimFact, new("durability-unknown"));
                    }
                    catch (Exception)
                    {
                        claimThrew = true;
                        claim = new GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown(claimFact, new("durability-unknown"));
                    }
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.HeadConflict)
                    {
                        refreshHead = true;
                        break;
                    }
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown)
                    {
                        GlobalParticipantAllocatorReconcileResultV1 reconciliation;
                        try { reconciliation = await _allocatorReconciliation.ReconcileAsync(new(_allocatorLease, claimFact), CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception) { return new GraphParticipantReservationResultV2.OutcomeUnknown(claimFact, new("allocator-outcome-unknown")); }
                        if (reconciliation is GlobalParticipantAllocatorReconcileResultV1.NotFound)
                        {
                            if (cancellationToken.IsCancellationRequested) return new GraphParticipantReservationResultV2.OutcomeUnknown(claimFact, new("allocator-outcome-unknown"));
                            continue;
                        }
                        if (reconciliation is GlobalParticipantAllocatorReconcileResultV1.Committed committedReconcile)
                        {
                            if (!ReturnedCandidateMatches(committedReconcile.Head, committedReconcile.Sequence, committedReconcile.ExactCanonicalRecordBytes, snapshot, claimOuterBytes, claimFact, outer, claimBody)) return Q("returned-candidate-mismatch");
                            if (cancellationToken.IsCancellationRequested) return new GraphParticipantReservationResultV2.OutcomeUnknown(claimFact, new("allocator-outcome-unknown"));
                            return await AppendResultAsync(command, body, participant, outcome, committedReconcile.Head, claimFact, request, cancellationToken).ConfigureAwait(false);
                        }
                        if (reconciliation is GlobalParticipantAllocatorReconcileResultV1.RealmFenced rf) return new GraphParticipantReservationResultV2.RealmFenced(rf.CurrentFenceEpoch);
                        if (reconciliation is GlobalParticipantAllocatorReconcileResultV1.StoreUnavailable && claimThrew) return new GraphParticipantReservationResultV2.OutcomeUnknown(claimFact, new("allocator-outcome-unknown"));
                        if (reconciliation is GlobalParticipantAllocatorReconcileResultV1.StoreUnavailable) return S("allocator-store-unavailable");
                        if (reconciliation is GlobalParticipantAllocatorReconcileResultV1.Quarantined) return Q("allocator-quarantined");
                        return new GraphParticipantReservationResultV2.OutcomeUnknown(claimFact, new("allocator-outcome-unknown"));
                    }
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.Committed committed)
                    {
                        if (!ReturnedCandidateMatches(committed.Head, committed.Sequence, committed.ExactCanonicalRecordBytes, snapshot, claimOuterBytes, claimFact, outer, claimBody)) return Q("returned-candidate-mismatch");
                        if (cancellationToken.IsCancellationRequested) return new GraphParticipantReservationResultV2.OutcomeUnknown(claimFact, new("allocator-outcome-unknown"));
                        return await AppendResultAsync(command, body, participant, outcome, committed.Head, claimFact, request, cancellationToken).ConfigureAwait(false);
                    }
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.AlreadyCommitted already)
                    {
                        if (!ReturnedCandidateMatches(already.Head, already.Sequence, already.ExactCanonicalRecordBytes, snapshot, claimOuterBytes, claimFact, outer, claimBody)) return Q("returned-candidate-mismatch");
                        if (cancellationToken.IsCancellationRequested) return new GraphParticipantReservationResultV2.OutcomeUnknown(claimFact, new("allocator-outcome-unknown"));
                        return await AppendResultAsync(command, body, participant, outcome, already.Head, claimFact, request, cancellationToken).ConfigureAwait(false);
                    }
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.RealmFenced rf2) return new GraphParticipantReservationResultV2.RealmFenced(rf2.CurrentFenceEpoch);
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.StoreUnavailable) return S("allocator-store-unavailable");
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.LifetimeExhausted) return Q("allocator-lifetime-exhausted");
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.InvalidRecord) return Q("allocator-record-invalid");
                    if (claim is GlobalParticipantAllocatorDurableClaimResultV1.ContradictoryDuplicate) return Q("contradictory-duplicate");
                    return Q("allocator-quarantined");
                }
                if (!refreshHead)
                    return new GraphParticipantReservationResultV2.OutcomeUnknown(claimFact, new("allocator-outcome-unknown"));
            }
            return new GraphParticipantReservationResultV2.RetryRequired(new("allocator-head-advanced"));
        }
        finally { _mutex.Release(); }
    }

    private async ValueTask<(GraphParticipantReservationFoldV2? Fold, long Through, ulong Count, ulong Bytes, JournalPositionV1? AppliedFact, GraphParticipantReservationResultV2? Result)> ReadAsync(SessionAuthorityStampV1 session, GraphParticipantReservationRequestV2 request, CancellationToken token)
    {
        var fold = GraphParticipantReservationFoldV2.Create(session); long cursor = 0; long? through = null; ulong count = 0, total = 0;
        while (through is null || cursor < through)
        {
            ReadAuthorityRangeResultV1 read;
            try { read = await _sessionJournal.ReadAsync(new(session, cursor, through ?? long.MaxValue, 256, 1_048_576), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return (null, 0, 0, 0, null, S("session-journal-store-unavailable")); }
            if (read is ReadAuthorityRangeResultV1.StoreUnavailable) return (null, 0, 0, 0, null, S("session-journal-store-unavailable"));
            if (read is ReadAuthorityRangeResultV1.ItemTooLarge) return (null, 0, 0, 0, null, Q("session-envelope-too-large"));
            if (read is not ReadAuthorityRangeResultV1.Batch b || b.AfterExclusive != cursor || through is not null && b.SnapshotThrough != through) return (null, 0, 0, 0, null, Q("session-history-invalid"));
            through ??= b.SnapshotThrough;
            foreach (var e in b.Facts) { count++; var length = AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(e); total += length; if (count > request.MaximumSessionRecords || total > request.MaximumSessionCanonicalBytes)return(null,0,0,0,null,Q("session-history-invalid"));if(fold.Apply(e) is GraphParticipantReservationFoldV2.InvalidHistory invalid){var code=invalid.SafeCode.ToString();return(null,0,0,0,null,Q(code=="cross-version-operation-conflict"?code:"session-history-invalid"));} }
            cursor = b.Facts.Count == 0 ? b.SnapshotThrough : b.Facts[^1].Position.Sequence;
        }
        try{var completed = fold.Complete(); return (fold, through ?? 0, completed.RecordCount, completed.TotalCanonicalBytes, completed.AppliedReservationFact, null);}catch(InvalidOperationException e) when(e.Message=="cross-version-command-incomplete"){return(null,0,0,0,null,Q("cross-version-command-incomplete"));}
    }
    private async ValueTask<(AuthorityFactEnvelopeV1? Envelope, GraphParticipantReservationResultV2? Result)> AppendCommandAsync(SessionAuthorityStampV1 session, OperationId operation, ProposedAuthorityFactV1 proposal, GraphParticipantReservationRequestV2 request, long head, CancellationToken token)
    {
        var factId = proposal.FactId;
        for (var i = 0; i < 3; i++)
        {
            token.ThrowIfCancellationRequested();
            var ambiguous = false;
            try
            {
                var batch = new AppendAuthorityBatchV1(session, head, [], [proposal], 1_048_576);
                if (AuthorityCanonicalCborV1.GetAppendBatchEncodedLength(batch) > 1_048_576UL) return (null, S("journal-capacity-refused"));
                var r = await _sessionJournal.AppendAsync(batch, token).ConfigureAwait(false);
                if (r is AppendAuthorityResultV1.Committed c)
                    return c.Envelopes.Count == 1 && EnvelopeMatches(c.Envelopes[0], proposal, session, head + 1) ? (c.Envelopes[0], null) : (null, Q("returned-candidate-mismatch"));
                if (r is AppendAuthorityResultV1.AlreadyCommitted a)
                    return a.Envelopes.Count == 1 && EnvelopeMatches(a.Envelopes[0], proposal, session, a.Envelopes[0].Position.Sequence) ? (a.Envelopes[0], null) : (null, Q("returned-candidate-mismatch"));
                if (r is AppendAuthorityResultV1.ThreadConflict) return (null, Q("unexpected-thread-conflict"));
                if (r is AppendAuthorityResultV1.ContradictoryDuplicate) return (null, Q("contradictory-duplicate"));
                if (r is AppendAuthorityResultV1.UnknownSchema) return (null, Q("schema-unavailable"));
                if (r is AppendAuthorityResultV1.InvalidPayload) return (null, Q("invalid-payload"));
                if (r is AppendAuthorityResultV1.CapacityRefused) return (null, S("journal-capacity-refused"));
                if (r is AppendAuthorityResultV1.StoreUnavailable) return (null, S("session-journal-store-unavailable"));
                ambiguous = r is AppendAuthorityResultV1.SessionConflict or AppendAuthorityResultV1.OutcomeUnknown;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                ambiguous = true;
            }
            catch (Exception)
            {
                ambiguous = true;
            }

            if (!ambiguous) return (null, Q("session-history-invalid"));
            var history = await ReadAsync(session, request, CancellationToken.None).ConfigureAwait(false);
            if (history.Result is not null)
                return (null, history.Result is GraphParticipantReservationResultV2.StoreUnavailable ? new GraphParticipantReservationResultV2.OutcomeUnknown(factId, new("session-journal-outcome-unknown")) : history.Result);
            if (history.Fold!.Query(operation) is GraphParticipantReservationFoldV2.CommandOnly foundCommand)
                return EnvelopeMatches(foundCommand.Command, proposal, session, foundCommand.Command.Position.Sequence) ? (foundCommand.Command, null) : (null, Q("returned-candidate-mismatch"));
            if (token.IsCancellationRequested)
                return (null, new GraphParticipantReservationResultV2.OutcomeUnknown(factId, new("session-journal-outcome-unknown")));
            head = history.Through;
        }
        return (null, new GraphParticipantReservationResultV2.OutcomeUnknown(factId, new("session-journal-outcome-unknown")));
    }
    private ValueTask<GraphParticipantReservationResultV2> RejectWithoutAllocatorAsync(AuthorityFactEnvelopeV1 command,GraphParticipantReservationCommandBodyV2 body,BoundedAscii code,JournalPositionV1? actualPredecessor,GraphParticipantReservationRequestV2 request,CancellationToken token)
    { token.ThrowIfCancellationRequested();return AppendResultAsync(command,body,default,null,default,GraphParticipantReservationFactIdsV2.ReservationFact(command.Position),request,token,code,actualPredecessor); }
    private async ValueTask<GraphParticipantReservationResultV2> AppendResultAsync(AuthorityFactEnvelopeV1 command, GraphParticipantReservationCommandBodyV2 body, ParticipantId participant, ParticipantIdClaimOutcomeV1? outcome, GlobalParticipantAuthorityHeadV1 allocatorHead, JournalFactId allocatorClaimFact, GraphParticipantReservationRequestV2 request, CancellationToken token,BoundedAscii? protocolCode=null,JournalPositionV1? protocolPredecessor=null)
    {
        var reservation = outcome?.Kind == 1 ? new GraphParticipantReservationV1(participant, body.ParticipantFactoryKey, body.OrderedTopologyNodeKeys) : null;
        var code = protocolCode??(outcome?.Kind == 2 ? new BoundedAscii("participant-id-collision") : (BoundedAscii?)null);
        var actualPredecessor=protocolCode is null?body.ExpectedReservationFact:protocolPredecessor;
        var factBody = new GraphParticipantReservationFactBodyV2(body.OperationId, command.Position, actualPredecessor, outcome?.Kind == 1 ? (ushort)1 : (ushort)2, body.RuntimeGeneration, body.GraphGeneration, body.ParticipantPlanFingerprint, body.AllocationCarrierFingerprint, reservation, code, body.ObservedAt);
        var bodyBytes = GraphParticipantReservationCodecsV2.Encode(factBody);
        var outer = GraphParticipantReservationCodecsV2.TryDecodeReservationCommand(command.PayloadMemory, out var commandOuter) && commandOuter is not null ? new GraphParticipantReservationFactV2(commandOuter.Session, commandOuter.ExpectedAuthority, bodyBytes) : throw new InvalidOperationException();
        var payload = GraphParticipantReservationCodecsV2.Encode(outer);
        var registration = GraphParticipantReservationPayloadRegistrationsV2.ReservationFact;
        var factId = GraphParticipantReservationFactIdsV2.ReservationFact(command.Position);
        var proposal = new ProposedAuthorityFactV1(factId, null, OwnerSliceId.S1, registration.Schema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), command.Correlation, command.ObservedAt);
        for (var i = 0; i < 3; i++)
        {
            if (token.IsCancellationRequested)
            {
                if (protocolCode is not null) token.ThrowIfCancellationRequested();
                return new GraphParticipantReservationResultV2.OutcomeUnknown(allocatorClaimFact, new("allocator-outcome-unknown"));
            }
            var history = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
            if (history.Result is not null) return history.Result;
            var q = history.Fold!.Query(body.OperationId);
            if (q is GraphParticipantReservationFoldV2.AppliedReservation appliedFound) return new GraphParticipantReservationResultV2.Applied(command.Position, appliedFound.Fact.Position, participant, allocatorHead, appliedFound.Fact.PayloadMemory);
            if (q is GraphParticipantReservationFoldV2.RejectedReservation rejectedFound) return new GraphParticipantReservationResultV2.Rejected(command.Position, rejectedFound.Fact.Position, rejectedFound.SafeCode, rejectedFound.Fact.PayloadMemory);
            if(protocolCode is null){if(!Nullable.Equals(history.AppliedFact,body.ExpectedReservationFact))return Q("session-singleton-changed");}
            else
            {var election=history.Fold.Elect(body.OperationId);var valid=protocolCode.Value.ToString() switch{"participant-already-reserved"=>election is GraphParticipantReservationFoldV2.Follower&&protocolPredecessor==body.ExpectedReservationFact||election is GraphParticipantReservationFoldV2.BlockedByApplied blocked&&protocolPredecessor==blocked.AppliedFact,"reservation-predecessor-conflict"=>election is GraphParticipantReservationFoldV2.PredecessorConflict conflict&&protocolPredecessor==conflict.ActualPredecessor,_=>false};if(!valid)return Q("session-singleton-changed");}
            if ((ulong)payload.Length + 4425UL > 8192UL || history.Count >= request.MaximumSessionRecords || history.Bytes > (request.MaximumSessionCanonicalBytes - (ulong)Math.Min(request.MaximumSessionCanonicalBytes, (ulong)payload.Length + 4425UL))) return S("journal-capacity-refused");
            if (token.IsCancellationRequested)
            {
                if (protocolCode is not null) token.ThrowIfCancellationRequested();
                return new GraphParticipantReservationResultV2.OutcomeUnknown(allocatorClaimFact, new("allocator-outcome-unknown"));
            }
            try
            {
                var batch = new AppendAuthorityBatchV1(command.Position.Session, history.Through, [], [proposal], 1_048_576);
                if (AuthorityCanonicalCborV1.GetAppendBatchEncodedLength(batch) > 1_048_576UL) return S("journal-capacity-refused");
                var append = await _sessionJournal.AppendAsync(batch, token).ConfigureAwait(false);
                AuthorityFactEnvelopeV1? accepted = append switch { AppendAuthorityResultV1.Committed c when c.Envelopes.Count == 1 && EnvelopeMatches(c.Envelopes[0], proposal, command.Position.Session, history.Through + 1) => c.Envelopes[0], AppendAuthorityResultV1.AlreadyCommitted a when a.Envelopes.Count == 1 && EnvelopeMatches(a.Envelopes[0], proposal, command.Position.Session, a.Envelopes[0].Position.Sequence) => a.Envelopes[0], _ => null };
                if (append is AppendAuthorityResultV1.Committed or AppendAuthorityResultV1.AlreadyCommitted)
                {
                    if (accepted is null) return Q("returned-candidate-mismatch");
                    return outcome?.Kind == 1 ? new GraphParticipantReservationResultV2.Applied(command.Position, accepted.Position, participant, allocatorHead, accepted.PayloadMemory) : new GraphParticipantReservationResultV2.Rejected(command.Position, accepted.Position, code!.Value, accepted.PayloadMemory);
                }
                if (append is AppendAuthorityResultV1.StoreUnavailable) return S("session-journal-store-unavailable");
                if (append is AppendAuthorityResultV1.CapacityRefused) return S("journal-capacity-refused");
                if (append is AppendAuthorityResultV1.ThreadConflict) return Q("unexpected-thread-conflict");
                if (append is AppendAuthorityResultV1.ContradictoryDuplicate) return Q("contradictory-duplicate");
                if (append is AppendAuthorityResultV1.UnknownSchema) return Q("schema-unavailable");
                if (append is AppendAuthorityResultV1.InvalidPayload) return Q("invalid-payload");
                if (append is not AppendAuthorityResultV1.SessionConflict and not AppendAuthorityResultV1.OutcomeUnknown) return Q("returned-candidate-mismatch");
                var reconciled = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
                if (reconciled.Result is not null) return new GraphParticipantReservationResultV2.OutcomeUnknown(factId, new("session-journal-outcome-unknown"));
                var reconciledQuery = reconciled.Fold!.Query(body.OperationId);
                if (reconciledQuery is GraphParticipantReservationFoldV2.AppliedReservation reconciledApplied) return new GraphParticipantReservationResultV2.Applied(command.Position, reconciledApplied.Fact.Position, participant, allocatorHead, reconciledApplied.Fact.PayloadMemory);
                if (reconciledQuery is GraphParticipantReservationFoldV2.RejectedReservation reconciledRejected) return new GraphParticipantReservationResultV2.Rejected(command.Position, reconciledRejected.Fact.Position, reconciledRejected.SafeCode, reconciledRejected.Fact.PayloadMemory);
                if (token.IsCancellationRequested) return new GraphParticipantReservationResultV2.OutcomeUnknown(factId, new("session-journal-outcome-unknown"));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                var h = await ReadAsync(command.Position.Session, request, CancellationToken.None).ConfigureAwait(false);
                if (h.Result is not null) return new GraphParticipantReservationResultV2.OutcomeUnknown(factId, new("session-journal-outcome-unknown"));
                var found = h.Fold!.Query(body.OperationId);
                if (found is GraphParticipantReservationFoldV2.AppliedReservation canceledApplied) return new GraphParticipantReservationResultV2.Applied(command.Position, canceledApplied.Fact.Position, participant, allocatorHead, canceledApplied.Fact.PayloadMemory);
                if (found is GraphParticipantReservationFoldV2.RejectedReservation canceledRejected) return new GraphParticipantReservationResultV2.Rejected(command.Position, canceledRejected.Fact.Position, canceledRejected.SafeCode, canceledRejected.Fact.PayloadMemory);
                return new GraphParticipantReservationResultV2.OutcomeUnknown(factId, new("session-journal-outcome-unknown"));
            }
            catch (Exception) { }
        }
        return new GraphParticipantReservationResultV2.OutcomeUnknown(factId, new("session-journal-outcome-unknown"));
    }
    private async ValueTask<GraphParticipantReservationResultV2> ExistingAppliedAsync(GraphParticipantReservationFoldV2.AppliedReservation x, CancellationToken token)
    {
        GlobalParticipantAllocatorDurableSnapshotResultV1 result;
        try { result = await _allocatorSnapshots.ReadAsync(new(_allocatorLease), token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return S("allocator-store-unavailable"); }
        if (result is GlobalParticipantAllocatorDurableSnapshotResultV1.RealmFenced f) return new GraphParticipantReservationResultV2.RealmFenced(f.CurrentFenceEpoch);
        if (result is GlobalParticipantAllocatorDurableSnapshotResultV1.StoreUnavailable) return S("allocator-store-unavailable");
        if (result is not GlobalParticipantAllocatorDurableSnapshotResultV1.Current current) return Q("allocator-snapshot-invalid");
        var snapshot = current.Snapshot; if (snapshot.JournalId != _allocatorLease.Manifest.JournalId) return Q("allocator-snapshot-invalid"); var fold = GlobalParticipantAllocatorFoldV1.Create(snapshot.JournalId);
        foreach (var record in snapshot.ExactCanonicalRecords) if (fold.Apply(record) is GlobalParticipantAllocatorFoldApplyResultV1.InvalidHistory) return Q("allocator-snapshot-invalid");
        if (fold.Complete() is not GlobalParticipantAllocatorFoldResultV1.Current completed || completed.Snapshot.Head != snapshot.Head || completed.Snapshot.RecordCount != snapshot.RecordCount || completed.Snapshot.TotalCanonicalRecordBytes != snapshot.TotalCanonicalRecordBytes) return Q("allocator-snapshot-invalid");
        if (!GraphParticipantReservationCodecsV2.TryDecodeReservationCommand(x.Command.PayloadMemory, out var commandOuter) || commandOuter is null || !GraphParticipantReservationCodecsV2.TryDecodeReservationCommandBody(commandOuter.BodyBytes.ToArray(), out var commandBody) || commandBody is null) return Q("allocator-snapshot-invalid");
        var source = new GlobalParticipantAllocationSourceV1(commandOuter.Session.LiveSessionId, x.Command.Position, x.Command.PayloadHash, Hash256.Compute(commandOuter.BodyBytes)); var sourceFingerprint = GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(source); var proof = completed.Snapshot.Query(x.Reservation.ParticipantId); var owner = proof.Owner;
        if (owner is null || owner.ParticipantId != x.Reservation.ParticipantId || owner.LiveSessionId != commandOuter.Session.LiveSessionId || owner.OperationId != commandBody.OperationId || owner.SourceFingerprint != sourceFingerprint || owner.ClaimHead.Position.Sequence is 0 || owner.ClaimHead.Position.Sequence > snapshot.RecordCount) return Q("allocator-snapshot-invalid");
        var ownerRecord = snapshot.ExactCanonicalRecords[checked((int)owner.ClaimHead.Position.Sequence - 1)];
        if (!GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(ownerRecord, out var claimOuter) || claimOuter is null || !GlobalParticipantAllocatorCodecsV1.TryDecodeBody(claimOuter.BodyBytes.ToArray(), out var claimBody) || claimBody is null || claimOuter.SourceSession != commandOuter.Session || claimOuter.SourceExpectedAuthority != commandOuter.ExpectedAuthority || claimBody.OperationId != commandBody.OperationId || claimBody.Source != source || claimBody.ParticipantId != x.Reservation.ParticipantId || claimBody.AssignedPosition != owner.ClaimHead.Position || claimBody.ObservedAt != commandBody.ObservedAt || claimBody.Outcome.Kind != 1) return Q("allocator-snapshot-invalid");
        var factId = GlobalParticipantAllocatorFactIdsV1.Fact(claimOuter.SourceSession.LiveSessionId, claimBody.OperationId, sourceFingerprint); var recordHash = GlobalParticipantAllocatorFactIdsV1.RecordHash(claimBody.AssignedPosition, claimBody.PriorHead, factId, claimOuter.SourceSession, claimOuter.SourceExpectedAuthority, claimOuter.BodyBytes);
        if (owner.ClaimHead != new GlobalParticipantAuthorityHeadV1(claimBody.AssignedPosition, recordHash)) return Q("allocator-snapshot-invalid");
        return new GraphParticipantReservationResultV2.Applied(x.Command.Position, x.Fact.Position, x.Reservation.ParticipantId, owner.ClaimHead, x.Fact.PayloadMemory);
    }
    private static GraphParticipantReservationResultV2 Existing(GraphParticipantReservationFoldV2.RejectedReservation x) => new GraphParticipantReservationResultV2.Rejected(x.Command.Position, x.Fact.Position, x.SafeCode, x.Fact.PayloadMemory);
    private static bool EnvelopeMatches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal, SessionAuthorityStampV1 session, long sequence) => envelope.FactId == proposal.FactId && envelope.Position == new JournalPositionV1(session, sequence) && envelope.ThreadScope is null && proposal.ThreadId is null && envelope.Owner == OwnerSliceId.S1 && proposal.Owner == OwnerSliceId.S1 && envelope.PayloadSchema == proposal.PayloadSchema && envelope.PayloadBytes.SequenceEqual(proposal.PayloadBytes) && envelope.PayloadHash == proposal.PayloadHash && envelope.Correlation == proposal.Correlation && envelope.ObservedAt == proposal.ObservedAt;
    private static bool ReturnedCandidateMatches(GlobalParticipantAuthorityHeadV1 returnedHead, ulong returnedSequence, ReadOnlyMemory<byte> returnedBytes, GlobalParticipantAllocatorExactRecordSnapshotV1 snapshot, ReadOnlyMemory<byte> candidateBytes, JournalFactId factId, GraphParticipantReservationCommandV2 sourceOuter, GlobalParticipantClaimRecordBodyV1 candidateBody)
    {
        if (returnedSequence != snapshot.RecordCount + 1 || !returnedBytes.Span.SequenceEqual(candidateBytes.Span)) return false;
        var expectedHash = GlobalParticipantAllocatorFactIdsV1.RecordHash(candidateBody.AssignedPosition, snapshot.Head, factId, sourceOuter.Session, sourceOuter.ExpectedAuthority, GlobalParticipantAllocatorCodecsV1.Encode(candidateBody));
        return returnedHead == new GlobalParticipantAuthorityHeadV1(candidateBody.AssignedPosition, expectedHash);
    }
    private static GraphParticipantReservationResultV2 Q(string code) => new GraphParticipantReservationResultV2.Quarantined(new(code));
    private static GraphParticipantReservationResultV2 S(string code) => new GraphParticipantReservationResultV2.StoreUnavailable(new(code));
}

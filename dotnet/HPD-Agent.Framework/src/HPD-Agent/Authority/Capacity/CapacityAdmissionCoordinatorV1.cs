namespace HPD.Agent.Authority;

internal abstract record CapacityAdmissionResultV1
{
    private CapacityAdmissionResultV1() { }
    internal sealed record Granted(AuthorityFactEnvelopeV1 Envelope, CapacityGrantSnapshotV1 Grant) : CapacityAdmissionResultV1;
    internal sealed record AlreadyGranted(AuthorityFactEnvelopeV1 Envelope, CapacityGrantSnapshotV1 Grant) : CapacityAdmissionResultV1;
    internal sealed record Settled(AuthorityFactEnvelopeV1 Envelope, CapacityGrantSnapshotV1 Grant) : CapacityAdmissionResultV1;
    internal sealed record Refused(BoundedAscii SafeCode) : CapacityAdmissionResultV1;
    internal sealed record DeadlineExpired : CapacityAdmissionResultV1;
    internal sealed record StaleAuthority(long SnapshotThrough) : CapacityAdmissionResultV1;
    internal sealed record RetryRequired(long ObservedHead) : CapacityAdmissionResultV1;
    internal sealed record ContradictoryDuplicate(JournalFactId FactId) : CapacityAdmissionResultV1;
    internal sealed record OutcomeUnknown(JournalFactId FactId, BoundedAscii SafeCode) : CapacityAdmissionResultV1;
}

internal static class CapacityAdmissionCoordinatorV1
{
    private const ushort ReadItems = AppendAuthorityBatchV1.MaximumItems;
    private const uint ReadBytes = ProposedAuthorityFactV1.MaximumPayloadBytes;
    private const uint AppendBytes = ProposedAuthorityFactV1.MaximumPayloadBytes;
    private static readonly CapacityReservationPayloadRegistrationV1 ReservationRegistration = new();
    private static readonly CapacitySettlementPayloadRegistrationV1 SettlementRegistration = new();

    internal static async ValueTask<CapacityAdmissionResultV1> ReserveAsync(
        IAuthorityJournalV1 journal,
        CapacityRequestV1 request,
        CapacityGrantExpiryV1 expiry,
        CorrelationEnvelopeV1 correlation,
        MonotonicStampV1 admissionTime,
        UtcInstant observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(expiry);
        if (!correlation.IsValid || correlation.OperationId != request.OperationId || !admissionTime.IsValid)
            throw new ArgumentException("Correlation must bind the reservation operation.", nameof(correlation));
        var deadlineComparison = admissionTime.CompareTo(request.Deadline);
        if (deadlineComparison == ClockComparison.Incomparable)
            throw new ArgumentException("Admission time must use the request clock and boot.", nameof(admissionTime));
        if (deadlineComparison == ClockComparison.Later)
            return new CapacityAdmissionResultV1.DeadlineExpired();
        var snapshot = await ReadSnapshotAsync(journal, request.Authority.Session, cancellationToken).ConfigureAwait(false);
        if (snapshot is not SnapshotResult.Verified verified) return Unknown(CapacityFactIdsV1.Reservation(CapacityGrantIdDerivationV1.Derive(request.OperationId)), "snapshot-unknown");
        var existing = verified.Grants.SingleOrDefault(grant => grant.OperationId == request.OperationId);
        var body = new CapacityReservationFactBodyV1(CapacityGrantIdDerivationV1.Derive(request.OperationId), request, expiry);
        var payload = CapacityLedgerCodecsV1.EncodeReservation(body);
        var hash = CapacityLedgerCodecsV1.ComputeReservationHash(body);
        var factId = CapacityFactIdsV1.Reservation(body.GrantId);
        if (existing is not null)
        {
            var envelope = verified.CapacityEnvelopes.Single(fact => fact.Position == existing.GrantedAt);
            return envelope.FactId == factId && envelope.PayloadHash == hash && envelope.Payload.SequenceEqual(payload)
                ? new CapacityAdmissionResultV1.AlreadyGranted(envelope, existing)
                : new CapacityAdmissionResultV1.ContradictoryDuplicate(factId);
        }
        if (!MatchesCurrent(request.Authority, verified.Authority)) return new CapacityAdmissionResultV1.StaleAuthority(verified.Head);
        var candidate = new CapacityLedgerEntryV1.Reservation(
            new JournalPositionV1(request.Authority.Session, checked(verified.Head + 1)), request.Authority.Session, request.Authority, body);
        if (CapacityLedgerReducerV1.Fold(verified.Entries.Append(candidate)) is CapacityLedgerFoldResultV1.InvalidHistory invalid)
            return new CapacityAdmissionResultV1.Refused(new BoundedAscii(invalid.SafeCode));
        var proposal = new ProposedAuthorityFactV1(factId, null, OwnerSliceId.S2, ReservationRegistration.Schema,
            payload, hash, correlation, observedAt);
        return await AppendAsync(journal, request.Authority.Session, verified.Head, proposal, body.GrantId, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<CapacityAdmissionResultV1> SettleAsync(
        IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session,
        CapacitySettlementFactBodyV1 body,
        CorrelationEnvelopeV1 correlation,
        UtcInstant observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(body);
        if (!session.IsValid || !correlation.IsValid || correlation.OperationId != body.OperationId)
            throw new ArgumentException("Session and correlation must bind the settlement operation.");
        var factId = CapacityFactIdsV1.Settlement(body.GrantId, body.OperationId);
        var snapshot = await ReadSnapshotAsync(journal, session, cancellationToken).ConfigureAwait(false);
        if (snapshot is not SnapshotResult.Verified verified) return Unknown(factId, "snapshot-unknown");
        var grant = verified.Grants.SingleOrDefault(value => value.GrantId == body.GrantId);
        if (grant is null) return new CapacityAdmissionResultV1.Refused(new BoundedAscii("grant-not-found"));
        var payload = CapacityLedgerCodecsV1.EncodeSettlement(body); var hash = CapacityLedgerCodecsV1.ComputeSettlementHash(body);
        var duplicate = verified.CapacityEnvelopes.SingleOrDefault(value => value.FactId == factId);
        if (duplicate is not null)
            return duplicate.PayloadHash == hash && duplicate.Payload.SequenceEqual(payload)
                ? new CapacityAdmissionResultV1.Settled(duplicate, grant)
                : new CapacityAdmissionResultV1.ContradictoryDuplicate(factId);
        var candidate = new CapacityLedgerEntryV1.Settlement(
            new JournalPositionV1(session, checked(verified.Head + 1)), session, grant.Authority, body);
        if (CapacityLedgerReducerV1.Fold(verified.Entries.Append(candidate)) is CapacityLedgerFoldResultV1.InvalidHistory invalid)
            return new CapacityAdmissionResultV1.Refused(new BoundedAscii(invalid.SafeCode));
        var proposal = new ProposedAuthorityFactV1(factId, null, OwnerSliceId.S2, SettlementRegistration.Schema,
            payload, hash, correlation, observedAt);
        return await AppendAsync(journal, session, verified.Head, proposal, body.GrantId, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<CapacityAdmissionResultV1> AppendAsync(IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session, long expectedHead, ProposedAuthorityFactV1 proposal, CapacityGrantId grantId,
        CancellationToken cancellationToken)
    {
        AppendAuthorityResultV1 result;
        try { result = await journal.AppendAsync(new AppendAuthorityBatchV1(session, expectedHead, [], [proposal], AppendBytes), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Unknown(proposal.FactId, "append-exception"); }
        if (result is AppendAuthorityResultV1.SessionConflict conflict) return new CapacityAdmissionResultV1.RetryRequired(conflict.Actual);
        if (result is AppendAuthorityResultV1.ContradictoryDuplicate) return new CapacityAdmissionResultV1.ContradictoryDuplicate(proposal.FactId);
        if (result is AppendAuthorityResultV1.StoreUnavailable unavailable) return new CapacityAdmissionResultV1.OutcomeUnknown(proposal.FactId, unavailable.SafeCode);
        if (result is AppendAuthorityResultV1.OutcomeUnknown) return Unknown(proposal.FactId, "append-outcome-unknown");
        var envelope = result switch
        {
            AppendAuthorityResultV1.Committed committed when committed.PreviousHead == expectedHead && committed.Envelopes.Count == 1 => committed.Envelopes[0],
            AppendAuthorityResultV1.AlreadyCommitted already when already.Envelopes.Count == 1 => already.Envelopes[0],
            _ => null,
        };
        if (envelope is null || !Matches(envelope, proposal, session))
            return result is AppendAuthorityResultV1.InvalidPayload invalid ? new CapacityAdmissionResultV1.Refused(invalid.SafeCode) : Unknown(proposal.FactId, "unexpected-append-result");
        var reread = await ReadSnapshotAsync(journal, session, cancellationToken).ConfigureAwait(false);
        if (reread is not SnapshotResult.Verified verified) return Unknown(proposal.FactId, "reconcile-unknown");
        var grant = verified.Grants.Single(value => value.GrantId == grantId);
        return proposal.PayloadSchema == ReservationRegistration.Schema
            ? result is AppendAuthorityResultV1.AlreadyCommitted ? new CapacityAdmissionResultV1.AlreadyGranted(envelope, grant) : new CapacityAdmissionResultV1.Granted(envelope, grant)
            : new CapacityAdmissionResultV1.Settled(envelope, grant);
    }

    private static bool MatchesCurrent(ExpectedAuthorityVectorV1 expected, CurrentAuthorityVectorSnapshotV1 current) =>
        expected.Session == current.Session && expected.Axes.All(entry => current.Axes.Any(actual => actual == entry));

    private static bool Matches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal, SessionAuthorityStampV1 session) =>
        envelope.Position.Session == session && envelope.FactId == proposal.FactId && envelope.ThreadScope is null &&
        envelope.Owner == proposal.Owner && envelope.PayloadSchema == proposal.PayloadSchema && envelope.PayloadHash == proposal.PayloadHash &&
        envelope.Payload.SequenceEqual(proposal.Payload) && envelope.Correlation == proposal.Correlation && envelope.ObservedAt == proposal.ObservedAt;

    private static CapacityAdmissionResultV1.OutcomeUnknown Unknown(JournalFactId factId, string code) => new(factId, new BoundedAscii(code));

    private abstract record SnapshotResult
    {
        private SnapshotResult() { }
        internal sealed record Unknown : SnapshotResult;
        internal sealed record Verified(long Head, CurrentAuthorityVectorSnapshotV1 Authority,
            IReadOnlyList<CapacityLedgerEntryV1> Entries, IReadOnlyList<AuthorityFactEnvelopeV1> CapacityEnvelopes,
            IReadOnlyList<CapacityGrantSnapshotV1> Grants) : SnapshotResult;
    }

    private static async ValueTask<SnapshotResult> ReadSnapshotAsync(IAuthorityJournalV1 journal, SessionAuthorityStampV1 session, CancellationToken cancellationToken)
    {
        var vector = AuthorityVectorReplayFoldV1.CreateAccumulator(session); var entries = new List<CapacityLedgerEntryV1>();
        var envelopes = new List<AuthorityFactEnvelopeV1>(); var authorities = new Dictionary<CapacityGrantId, ExpectedAuthorityVectorV1>();
        long cursor = 0; long? through = null;
        while (true)
        {
            ReadAuthorityRangeResultV1 result;
            try { result = await journal.ReadAsync(new ReadAuthorityRangeV1(session, cursor, through ?? long.MaxValue, ReadItems, ReadBytes), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return new SnapshotResult.Unknown(); }
            if (result is not ReadAuthorityRangeResultV1.Batch batch || batch.AfterExclusive != cursor || (through is not null && batch.SnapshotThrough != through)) return new SnapshotResult.Unknown();
            through ??= batch.SnapshotThrough;
            foreach (var fact in batch.Facts)
            {
                vector.Apply(fact); cursor = fact.Position.Sequence;
                if (fact.PayloadSchema == ReservationRegistration.Schema)
                {
                    if (fact.Owner != OwnerSliceId.S2 || !CapacityLedgerCodecsV1.TryDecodeReservation(fact.PayloadMemory, out var body)) return new SnapshotResult.Unknown();
                    entries.Add(new CapacityLedgerEntryV1.Reservation(fact.Position, session, body!.Request.Authority, body));
                    authorities[body.GrantId] = body.Request.Authority; envelopes.Add(fact);
                }
                else if (fact.PayloadSchema == SettlementRegistration.Schema)
                {
                    if (fact.Owner != OwnerSliceId.S2 || !CapacityLedgerCodecsV1.TryDecodeSettlement(fact.PayloadMemory, out var body) || !authorities.TryGetValue(body!.GrantId, out var authority)) return new SnapshotResult.Unknown();
                    entries.Add(new CapacityLedgerEntryV1.Settlement(fact.Position, session, authority, body)); envelopes.Add(fact);
                }
            }
            if (batch.HasMore) { if (batch.Facts.Count == 0) return new SnapshotResult.Unknown(); continue; }
            if (cursor != through) return new SnapshotResult.Unknown();
            var replay = vector.Complete(); var fold = CapacityLedgerReducerV1.Fold(entries);
            return replay is AuthorityVectorReplayResultV1.Current current && fold is CapacityLedgerFoldResultV1.Current capacity
                ? new SnapshotResult.Verified(through.Value, current.Snapshot, entries.AsReadOnly(), envelopes.AsReadOnly(), capacity.Grants)
                : new SnapshotResult.Unknown();
        }
    }
}

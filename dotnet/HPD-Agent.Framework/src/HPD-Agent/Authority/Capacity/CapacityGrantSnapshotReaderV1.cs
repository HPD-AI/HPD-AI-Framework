namespace HPD.Agent.Authority;

internal abstract record CapacityGrantSnapshotAtResultV1
{
    private CapacityGrantSnapshotAtResultV1() { }
    internal sealed record Exact : CapacityGrantSnapshotAtResultV1
    {
        internal Exact(CapacityGrantSnapshotV1 grant, int factsExamined, ulong canonicalEnvelopeBytesExamined)
        {
            Grant = grant ?? throw new ArgumentNullException(nameof(grant));
            if (factsExamined <= 0) throw new ArgumentOutOfRangeException(nameof(factsExamined));
            if (canonicalEnvelopeBytesExamined == 0) throw new ArgumentOutOfRangeException(nameof(canonicalEnvelopeBytesExamined));
            FactsExamined = factsExamined; CanonicalEnvelopeBytesExamined = canonicalEnvelopeBytesExamined;
        }
        internal CapacityGrantSnapshotV1 Grant { get; }
        internal int FactsExamined { get; }
        internal ulong CanonicalEnvelopeBytesExamined { get; }
    }
    internal sealed record OutcomeUnknown : CapacityGrantSnapshotAtResultV1
    {
        internal OutcomeUnknown(BoundedAscii safeCode) => SafeCode = safeCode.IsValid
            ? safeCode : throw new ArgumentException("A safe code is required.", nameof(safeCode));
        internal BoundedAscii SafeCode { get; }
    }
}

internal static class CapacityGrantSnapshotReaderV1
{
    private const ushort MaximumPageItems = AppendAuthorityBatchV1.MaximumItems;
    private const uint MaximumPageBytes = 65_536;
    private const int MaximumFoldedFacts = 65_536;
    private static readonly CapacityReservationPayloadRegistrationV1 Reservation = new();
    private static readonly CapacitySettlementPayloadRegistrationV1 Settlement = new();

    internal static async ValueTask<CapacityGrantSnapshotAtResultV1> ReadAtAsync(
        IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session,
        CapacityGrantId grantId,
        JournalPositionV1 throughPosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!session.IsValid || !grantId.IsValid || !throughPosition.IsValid || throughPosition.Session != session)
            throw new ArgumentException("A valid same-session grant and historical position are required.");

        var entries = new List<CapacityLedgerEntryV1>();
        var authorities = new Dictionary<CapacityGrantId, ExpectedAuthorityVectorV1>();
        long cursor = 0; long? pinned = null; var folded = 0; ulong encodedBytes = 0;
        while (cursor < throughPosition.Sequence)
        {
            ReadAuthorityRangeResultV1 result;
            try
            {
                result = await journal.ReadAsync(new ReadAuthorityRangeV1(
                    session, cursor, throughPosition.Sequence, MaximumPageItems, MaximumPageBytes), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Unknown("capacity-history-read-failed"); }

            if (result is not ReadAuthorityRangeResultV1.Batch batch || batch.AfterExclusive != cursor ||
                batch.SnapshotThrough != throughPosition.Sequence || pinned is not null && pinned != batch.SnapshotThrough)
                return Unknown("capacity-history-snapshot-drift");
            pinned ??= batch.SnapshotThrough;
            if (batch.Facts.Count == 0 && batch.HasMore) return Unknown("capacity-history-empty-page");
            foreach (var fact in batch.Facts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++folded > MaximumFoldedFacts || fact.Position.Session != session || fact.Position.Sequence != cursor + 1 ||
                    fact.Position.Sequence > throughPosition.Sequence)
                    return Unknown("capacity-history-gap-or-bound");
                cursor = fact.Position.Sequence;
                encodedBytes = checked(encodedBytes + AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(fact));
                if (fact.PayloadSchema == Reservation.Schema)
                {
                    if (!TryReservation(fact, session, out var body)) return Unknown("capacity-reservation-invalid");
                    entries.Add(new CapacityLedgerEntryV1.Reservation(fact.Position, session, body!.Request.Authority, body));
                    if (!authorities.TryAdd(body.GrantId, body.Request.Authority)) return Unknown("capacity-grant-duplicate");
                }
                else if (fact.PayloadSchema == Settlement.Schema)
                {
                    if (!TrySettlement(fact, session, out var body) || !authorities.TryGetValue(body!.GrantId, out var authority))
                        return Unknown("capacity-settlement-invalid");
                    entries.Add(new CapacityLedgerEntryV1.Settlement(fact.Position, session, authority, body));
                }
                else if (fact.PayloadSchema.SchemaId == Reservation.Schema.SchemaId ||
                    fact.PayloadSchema.SchemaId == Settlement.Schema.SchemaId)
                    return Unknown("capacity-schema-version-invalid");
            }
            if (!batch.HasMore) break;
        }
        if (cursor != throughPosition.Sequence) return Unknown("capacity-history-incomplete");
        if (CapacityLedgerReducerV1.Fold(entries) is not CapacityLedgerFoldResultV1.Current current)
            return Unknown("capacity-history-fold-invalid");
        var matches = current.Grants.Where(value => value.GrantId == grantId).ToArray();
        if (matches.Length == 0) return Unknown("capacity-history-grant-not-observed");
        if (matches.Length != 1 || matches[0].CurrentFact != throughPosition)
            return Unknown("capacity-history-position-mismatch");
        return new CapacityGrantSnapshotAtResultV1.Exact(matches[0], folded, encodedBytes);
    }

    private static bool TryReservation(AuthorityFactEnvelopeV1 fact, SessionAuthorityStampV1 session,
        out CapacityReservationFactBodyV1? body)
    {
        body = null;
        if (fact.Owner != OwnerSliceId.S2 || fact.ThreadScope is not null ||
            !CapacityLedgerCodecsV1.TryDecodeReservation(fact.PayloadMemory, out body) || body is null ||
            body.Request.Authority.Session != session ||
            fact.PayloadHash != CapacityLedgerCodecsV1.ComputeReservationHash(body) ||
            fact.FactId != CapacityFactIdsV1.Reservation(body.GrantId)) return false;
        return fact.Payload.SequenceEqual(CapacityLedgerCodecsV1.EncodeReservation(body));
    }

    private static bool TrySettlement(AuthorityFactEnvelopeV1 fact, SessionAuthorityStampV1 session,
        out CapacitySettlementFactBodyV1? body)
    {
        body = null;
        if (fact.Owner != OwnerSliceId.S2 || fact.ThreadScope is not null ||
            !CapacityLedgerCodecsV1.TryDecodeSettlement(fact.PayloadMemory, out body) || body is null ||
            fact.Position.Session != session || fact.PayloadHash != CapacityLedgerCodecsV1.ComputeSettlementHash(body) ||
            fact.FactId != CapacityFactIdsV1.Settlement(body.GrantId, body.OperationId)) return false;
        return fact.Payload.SequenceEqual(CapacityLedgerCodecsV1.EncodeSettlement(body));
    }

    private static CapacityGrantSnapshotAtResultV1.OutcomeUnknown Unknown(string code) => new(new BoundedAscii(code));

}

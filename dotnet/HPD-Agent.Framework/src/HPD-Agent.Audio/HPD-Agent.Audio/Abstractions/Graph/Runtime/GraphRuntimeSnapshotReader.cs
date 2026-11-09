using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal abstract record GraphRuntimeSnapshotReadResult
{
    private GraphRuntimeSnapshotReadResult() { }
    internal sealed record Verified : GraphRuntimeSnapshotReadResult
    {
        internal Verified(GraphRuntimeJournalFoldResult fold, long snapshotThrough)
        {
            ArgumentNullException.ThrowIfNull(fold);
            var covered = fold switch
            {
                GraphRuntimeJournalFoldResult.Current x => x.SnapshotThrough,
                GraphRuntimeJournalFoldResult.RuntimeReplaced x => x.SnapshotThrough,
                GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced x => x.SnapshotThrough,
                _ => throw new ArgumentException("A complete non-invalid runtime fold is required.", nameof(fold)),
            };
            if (snapshotThrough < 0 || covered != snapshotThrough)
                throw new ArgumentException("The fold must cover the exact primary pin.", nameof(snapshotThrough));
            Fold = fold; SnapshotThrough = snapshotThrough;
        }
        internal GraphRuntimeJournalFoldResult Fold { get; }
        internal long SnapshotThrough { get; }
    }
    internal sealed record InvalidHistory : GraphRuntimeSnapshotReadResult
    {
        internal InvalidHistory(BoundedAscii code, long lastVerified)
        { if (!code.IsValid || lastVerified < 0) throw new ArgumentException("Valid invalid-history evidence is required."); Code=code;LastVerified=lastVerified; }
        internal BoundedAscii Code { get; } internal long LastVerified { get; }
    }
    internal sealed record OutcomeUnknown : GraphRuntimeSnapshotReadResult
    {
        internal OutcomeUnknown(BoundedAscii code,long lastVerified,PendingGraphRuntimeCommand? pending)
        { if(!code.IsValid||lastVerified<0||pending is not null&&pending.Operation.CommandEnvelope.Position.Sequence>lastVerified)throw new ArgumentException("Valid bounded uncertainty evidence is required.");Code=code;LastVerified=lastVerified;Pending=pending; }
        internal BoundedAscii Code{get;} internal long LastVerified{get;} internal PendingGraphRuntimeCommand? Pending{get;}
    }
}

internal static class GraphRuntimeSnapshotReader
{
    internal delegate ValueTask<CapacityGrantSnapshotAtResultV1> HistoricalProofReader(
        IAuthorityJournalV1 journal, SessionAuthorityStampV1 session, CapacityGrantId grantId,
        JournalPositionV1 through, CancellationToken cancellationToken);
    internal const ushort PageFacts = 256;
    internal const uint PageBytes = 1_048_576;
    internal const int MaximumFacts = 65_536;
    internal const int MaximumProofReads = 256;
    internal const long MaximumProofFacts = 65_536;
    internal const ulong MaximumProofBytes = 67_108_864;

    internal static async ValueTask<GraphRuntimeSnapshotReadResult> ReadAsync(IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session, CancellationToken cancellationToken = default) =>
        await ReadAsync(journal, session, CapacityGrantSnapshotReaderV1.ReadAtAsync, cancellationToken).ConfigureAwait(false);

    internal static async ValueTask<GraphRuntimeSnapshotReadResult> ReadAsync(IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session, HistoricalProofReader proofReader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(proofReader);
        if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
        var fold = GraphRuntimeJournalFold.CreateAccumulator(session);
        var proofs = new GraphRuntimeProofBudget();
        long cursor = 0; long? pin = null; var factCount = 0;
        while (true)
        {
            ReadAuthorityRangeResultV1 read;
            try
            {
                if (cancellationToken.IsCancellationRequested) return Unknown("runtime-read-cancelled", fold);
                read = await journal.ReadAsync(new ReadAuthorityRangeV1(session, cursor, pin ?? long.MaxValue,
                    PageFacts, PageBytes), cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) return Unknown("runtime-read-cancelled", fold);
            }
            catch (OperationCanceledException) { return Unknown("runtime-read-cancelled", fold); }
            catch (Exception) { return Unknown("runtime-store-exception", fold); }
            if (read is ReadAuthorityRangeResultV1.StoreUnavailable unavailable)
                return new GraphRuntimeSnapshotReadResult.OutcomeUnknown(unavailable.SafeCode,
                    fold.LastVerifiedPosition, Pending(fold));
            if (read is ReadAuthorityRangeResultV1.ItemTooLarge) return Unknown("runtime-item-too-large", fold);
            if (read is not ReadAuthorityRangeResultV1.Batch batch) return Unknown("runtime-read-result-unknown", fold);
            pin ??= batch.SnapshotThrough;
            if (batch.Session != session || batch.AfterExclusive != cursor || batch.SnapshotThrough != pin)
                return Unknown("runtime-snapshot-drift", fold);
            if (batch.Facts.Count > PageFacts) return Unknown("runtime-page-fact-bound", fold);
            ulong primaryBytes = 0;
            try
            {
                foreach (var fact in batch.Facts)
                    primaryBytes = checked(primaryBytes + AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(fact));
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            { return Unknown("runtime-page-envelope-invalid", fold); }
            if (primaryBytes > PageBytes) return Unknown("runtime-page-byte-bound", fold);
            if (batch.Facts.Count == 0 && batch.HasMore) return Unknown("runtime-empty-continuation", fold);
            foreach (var envelope in batch.Facts)
            {
                if (++factCount > MaximumFacts) return Unknown("runtime-fact-bound", fold);
                var inspected = fold.Inspect(envelope);
                CapacityGrantSnapshotV1? graphProof = null; CapacityGrantSnapshotV1? runtimeProof = null;
                if (fold.GraphCapacityReference(inspected) is { } graphReference)
                {
                    if (graphReference.Through.Session != session || graphReference.Through.Sequence >= envelope.Position.Sequence ||
                        graphReference.Through.Sequence > pin.Value)
                        return new GraphRuntimeSnapshotReadResult.InvalidHistory(Code("runtime-graph-proof-noncausal"), fold.LastVerifiedPosition);
                    var proof = await Proof(graphReference.GrantId, graphReference.Through).ConfigureAwait(false);
                    if (proof is null) return Unknown("runtime-graph-proof-unknown", fold);
                    if (cancellationToken.IsCancellationRequested) return Unknown("runtime-proof-cancelled", fold);
                    graphProof = proof;
                }
                if (inspected is GraphRuntimeJournalInspection.Command { Body: GraphRuntimeCommand.Activate activate })
                {
                    if (activate.CapacityGrantFact.Session != session || activate.CapacityGrantFact.Sequence >= envelope.Position.Sequence || activate.CapacityGrantFact.Sequence > pin)
                        return new GraphRuntimeSnapshotReadResult.InvalidHistory(Code("runtime-activation-proof-noncausal"), fold.LastVerifiedPosition);
                    if (fold.InstalledCapacityGrantId is not { } grantId)
                        return Unknown("runtime-installed-grant-unknown", fold);
                    runtimeProof = await Proof(grantId, activate.CapacityGrantFact).ConfigureAwait(false);
                    if (runtimeProof is null) return Unknown("runtime-activation-proof-unknown", fold);
                    if (cancellationToken.IsCancellationRequested) return Unknown("runtime-proof-cancelled", fold);
                }
                if (cancellationToken.IsCancellationRequested) return Unknown("runtime-read-cancelled", fold);
                fold.Apply(inspected, graphProof is null && runtimeProof is null ? null : new(graphProof, runtimeProof));
                if (fold.Failure is { } invalid)
                    return new GraphRuntimeSnapshotReadResult.InvalidHistory(invalid.Code, invalid.LastVerified);
                cursor = envelope.Position.Sequence;
            }
            if (!batch.HasMore)
            {
                if (cancellationToken.IsCancellationRequested) return Unknown("runtime-read-cancelled", fold);
                if (cursor != pin.Value) return Unknown("runtime-snapshot-incomplete", fold);
                var complete = fold.Complete();
                return complete is GraphRuntimeJournalFoldResult.InvalidHistory invalid
                    ? new GraphRuntimeSnapshotReadResult.InvalidHistory(invalid.Code, invalid.LastVerified)
                    : new GraphRuntimeSnapshotReadResult.Verified(complete, pin.Value);
            }

            async ValueTask<CapacityGrantSnapshotV1?> Proof(CapacityGrantId grantId, JournalPositionV1 through)
            {
                var key = (grantId, through);
                if (proofs.TryGet(key, out var cached)) return cached;
                if (!proofs.TryBeginRead()) return null;
                CapacityGrantSnapshotAtResultV1 result;
                try { result = await proofReader(journal, session, grantId, through,
                    cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return null; }
                catch (Exception) { return null; }
                if (result is not CapacityGrantSnapshotAtResultV1.Exact exact) return null;
                if (!proofs.TryAccept(key, exact)) return null;
                return exact.Grant;
            }
        }
    }

    private static PendingGraphRuntimeCommand? Pending(GraphRuntimeJournalFold.Accumulator fold) => fold.Complete() switch
    {
        GraphRuntimeJournalFoldResult.Current current => current.Pending,
        GraphRuntimeJournalFoldResult.RuntimeReplaced terminal => terminal.Pending,
        GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced terminal => terminal.Pending,
        _ => null,
    };
    private static GraphRuntimeSnapshotReadResult.OutcomeUnknown Unknown(string code,
        GraphRuntimeJournalFold.Accumulator fold) => new(Code(code), fold.LastVerifiedPosition, Pending(fold));
    private static BoundedAscii Code(string value) => new(value);
}

internal sealed class GraphRuntimeProofBudget
{
    private readonly Dictionary<(CapacityGrantId, JournalPositionV1), CapacityGrantSnapshotV1> _cache = [];
    internal int Reads { get; private set; }
    internal long Facts { get; private set; }
    internal ulong Bytes { get; private set; }
    internal bool TryGet((CapacityGrantId, JournalPositionV1) key, out CapacityGrantSnapshotV1 grant) =>
        _cache.TryGetValue(key, out grant!);
    internal bool TryBeginRead()
    {
        if (Reads == GraphRuntimeSnapshotReader.MaximumProofReads) return false;
        Reads++; return true;
    }
    internal bool TryAccept((CapacityGrantId, JournalPositionV1) key, CapacityGrantSnapshotAtResultV1.Exact exact)
    {
        ArgumentNullException.ThrowIfNull(exact);
        if (exact.Grant.GrantId != key.Item1 || exact.Grant.CurrentFact != key.Item2) return false;
        long facts; ulong bytes;
        try
        {
            facts = checked(Facts + exact.FactsExamined);
            bytes = checked(Bytes + exact.CanonicalEnvelopeBytesExamined);
        }
        catch (OverflowException) { return false; }
        if (facts > GraphRuntimeSnapshotReader.MaximumProofFacts || bytes > GraphRuntimeSnapshotReader.MaximumProofBytes)
            return false;
        if (!_cache.TryAdd(key, exact.Grant)) throw new InvalidOperationException("A cached proof cannot be admitted twice.");
        Facts = facts; Bytes = bytes; return true;
    }
}

using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal abstract record GraphReplacementSnapshotReadResultV1
{
    private GraphReplacementSnapshotReadResultV1() { }

    internal sealed record Verified : GraphReplacementSnapshotReadResultV1
    {
        internal Verified(GraphReplacementJournalFoldResultV1 fold, long snapshotThrough)
        {
            ArgumentNullException.ThrowIfNull(fold);
            if (snapshotThrough < 0) throw new ArgumentOutOfRangeException(nameof(snapshotThrough));
            var covered = fold switch
            {
                GraphReplacementJournalFoldResultV1.Current current => current.SnapshotThrough,
                GraphReplacementJournalFoldResultV1.RuntimeReplaced replaced => replaced.LastPosition,
                _ => throw new ArgumentException("Only a complete graph fold can be verified.", nameof(fold)),
            };
            if (covered != snapshotThrough)
                throw new ArgumentException("The fold must cover the exact pinned snapshot.", nameof(fold));
            Fold = fold;
            SnapshotThrough = snapshotThrough;
        }

        internal GraphReplacementJournalFoldResultV1 Fold { get; }
        internal long SnapshotThrough { get; }
    }

    internal sealed record OutcomeUnknown : GraphReplacementSnapshotReadResultV1
    {
        internal OutcomeUnknown(BoundedAscii safeCode, long lastVerifiedPosition)
        {
            if (!safeCode.IsValid) throw new ArgumentException("A safe code is required.", nameof(safeCode));
            if (lastVerifiedPosition < 0) throw new ArgumentOutOfRangeException(nameof(lastVerifiedPosition));
            SafeCode = safeCode;
            LastVerifiedPosition = lastVerifiedPosition;
        }

        internal BoundedAscii SafeCode { get; }
        internal long LastVerifiedPosition { get; }
    }
}

internal static class GraphReplacementSnapshotReaderV1
{
    private const int MaximumFoldedFacts = 65_536;

    internal static async ValueTask<GraphReplacementSnapshotReadResultV1> ReadAsync(
        IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session,
        JournalFactId? targetCommandFactId = null,
        ushort maximumFacts = AppendAuthorityBatchV1.MaximumItems,
        uint maximumEncodedBytes = ProposedAuthorityFactV1.MaximumPayloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
        if (targetCommandFactId is { IsValid: false })
            throw new ArgumentException("A present target command identity must be valid.", nameof(targetCommandFactId));
        if (maximumFacts is 0 or > AppendAuthorityBatchV1.MaximumItems)
            throw new ArgumentOutOfRangeException(nameof(maximumFacts));
        if (maximumEncodedBytes is 0 or > ProposedAuthorityFactV1.MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));

        var accumulator = GraphReplacementJournalFoldV1.CreateAccumulator(session, targetCommandFactId);
        var cursor = 0L;
        var folded = 0;
        long? pinned = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadAuthorityRangeResultV1 result;
            try
            {
                result = await journal.ReadAsync(new ReadAuthorityRangeV1(
                    session, cursor, pinned ?? long.MaxValue, maximumFacts, maximumEncodedBytes),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Unknown("graph-store-exception", accumulator.LastVerifiedPosition); }

            switch (result)
            {
                case ReadAuthorityRangeResultV1.StoreUnavailable unavailable:
                    return new GraphReplacementSnapshotReadResultV1.OutcomeUnknown(
                        unavailable.SafeCode, accumulator.LastVerifiedPosition);
                case ReadAuthorityRangeResultV1.ItemTooLarge:
                    return Unknown("graph-item-too-large", accumulator.LastVerifiedPosition);
                case ReadAuthorityRangeResultV1.Batch batch:
                    pinned ??= batch.SnapshotThrough;
                    if (batch.Session != session || batch.AfterExclusive != cursor || batch.SnapshotThrough != pinned)
                        return Unknown("graph-snapshot-drift", accumulator.LastVerifiedPosition);
                    if (batch.Facts.Count > maximumFacts)
                        return Unknown("graph-count-bound", accumulator.LastVerifiedPosition);
                    foreach (var envelope in batch.Facts)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++folded > MaximumFoldedFacts)
                            return Unknown("graph-fact-bound", accumulator.LastVerifiedPosition);
                        var inspected = accumulator.Inspect(envelope);
                        CapacityGrantSnapshotV1? proof = null;
                        if (inspected.CapacityReference is { } reference)
                        {
                            if (reference.Session != session || reference.Sequence >= envelope.Position.Sequence ||
                                reference.Sequence > pinned.Value)
                                return Unknown("graph-capacity-reference-noncausal", accumulator.LastVerifiedPosition);
                            var grantId = accumulator.CapacityGrantFor(inspected);
                            if (grantId is null)
                                return Unknown("graph-capacity-reference-invalid", accumulator.LastVerifiedPosition);
                            var capacity = await CapacityGrantSnapshotReaderV1.ReadAtAsync(
                                journal, session, grantId.Value, reference, cancellationToken).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (capacity is not CapacityGrantSnapshotAtResultV1.Exact exact)
                                return Unknown("graph-capacity-proof-unknown", accumulator.LastVerifiedPosition);
                            proof = exact.Grant;
                        }
                        accumulator.Apply(inspected, proof);
                        if (accumulator.Failure is { } failure)
                            return new GraphReplacementSnapshotReadResultV1.OutcomeUnknown(
                                failure.SafeCode, failure.LastVerifiedPosition);
                        if (accumulator.RuntimeReplacement is { } replacement)
                        {
                            if (pinned.Value != replacement.LastPosition)
                                return Unknown("facts-after-runtime-replacement", replacement.LastPosition);
                            return new GraphReplacementSnapshotReadResultV1.Verified(replacement, pinned.Value);
                        }
                        cursor = envelope.Position.Sequence;
                    }
                    if (!batch.HasMore)
                    {
                        if (cursor != pinned.Value)
                            return Unknown("graph-snapshot-incomplete", accumulator.LastVerifiedPosition);
                        return Complete(accumulator, pinned.Value);
                    }
                    if (batch.Facts.Count == 0)
                        return Unknown("graph-empty-continuation", accumulator.LastVerifiedPosition);
                    break;
                default:
                    return Unknown("graph-read-result-unknown", accumulator.LastVerifiedPosition);
            }
        }
    }

    private static GraphReplacementSnapshotReadResultV1 Complete(
        GraphReplacementJournalFoldV1.Accumulator accumulator, long pinned)
    {
        return accumulator.Complete() switch
        {
            GraphReplacementJournalFoldResultV1.Current current =>
                new GraphReplacementSnapshotReadResultV1.Verified(current, pinned),
            GraphReplacementJournalFoldResultV1.RuntimeReplaced replaced =>
                new GraphReplacementSnapshotReadResultV1.Verified(replaced, pinned),
            GraphReplacementJournalFoldResultV1.AtomicCommitIncomplete incomplete =>
                Unknown("graph-commit-pair-incomplete", incomplete.LastVerifiedPosition),
            GraphReplacementJournalFoldResultV1.InvalidHistory invalid =>
                new GraphReplacementSnapshotReadResultV1.OutcomeUnknown(invalid.SafeCode, invalid.LastVerifiedPosition),
            _ => Unknown("graph-fold-result-unknown", accumulator.LastVerifiedPosition),
        };
    }

    private static GraphReplacementSnapshotReadResultV1.OutcomeUnknown Unknown(string code, long position) =>
        new(new BoundedAscii(code), position);
}

namespace HPD.Agent.Authority;

internal abstract record SessionLifecycleSnapshotReadResultV1
{
    private SessionLifecycleSnapshotReadResultV1() { }

    internal sealed record Verified : SessionLifecycleSnapshotReadResultV1
    {
        internal Verified(SessionLifecycleJournalFoldResultV1 fold, long snapshotThrough)
        {
            ArgumentNullException.ThrowIfNull(fold);
            if (snapshotThrough < 0) throw new ArgumentOutOfRangeException(nameof(snapshotThrough));
            var last = fold switch
            {
                SessionLifecycleJournalFoldResultV1.Current current => current.SnapshotThrough,
                SessionLifecycleJournalFoldResultV1.GenerationReplaced replaced => replaced.LastPosition,
                SessionLifecycleJournalFoldResultV1.InvalidHistory invalid => invalid.LastVerifiedPosition,
                _ => throw new ArgumentException("The fold is outside the closed lifecycle result union.", nameof(fold)),
            };
            if (last < 0 || (fold is SessionLifecycleJournalFoldResultV1.InvalidHistory ? last > snapshotThrough : last != snapshotThrough))
                throw new ArgumentException("The lifecycle fold must cover the pinned snapshot or an exact verified prefix.", nameof(fold));
            Fold = fold;
            SnapshotThrough = snapshotThrough;
        }

        internal SessionLifecycleJournalFoldResultV1 Fold { get; }
        internal long SnapshotThrough { get; }
    }

    internal sealed record OutcomeUnknown : SessionLifecycleSnapshotReadResultV1
    {
        internal OutcomeUnknown(BoundedAscii safeCode, long lastVerifiedPosition)
        {
            if (!safeCode.IsValid) throw new ArgumentException("A bounded safe code is required.", nameof(safeCode));
            if (lastVerifiedPosition < 0) throw new ArgumentOutOfRangeException(nameof(lastVerifiedPosition));
            SafeCode = safeCode;
            LastVerifiedPosition = lastVerifiedPosition;
        }

        internal BoundedAscii SafeCode { get; }
        internal long LastVerifiedPosition { get; }
    }
}

internal static class SessionLifecycleSnapshotReaderV1
{
    internal static async ValueTask<SessionLifecycleSnapshotReadResultV1> ReadAsync(
        IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session,
        JournalFactId? targetCommandFactId = null,
        ushort maximumFacts = AppendAuthorityBatchV1.MaximumItems,
        uint maximumEncodedBytes = ProposedAuthorityFactV1.MaximumPayloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!session.IsValid) throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        if (maximumFacts is 0 or > AppendAuthorityBatchV1.MaximumItems) throw new ArgumentOutOfRangeException(nameof(maximumFacts));
        if (maximumEncodedBytes is 0 or > ProposedAuthorityFactV1.MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
        if (targetCommandFactId is { IsValid: false })
            throw new ArgumentException("A present target command identity must be valid.", nameof(targetCommandFactId));
        var accumulator = SessionLifecycleJournalFoldV1.CreateAccumulator(session, targetCommandFactId);
        var cursor = 0L;
        long? snapshotThrough = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadAuthorityRangeResultV1 result;
            try
            {
                result = await journal.ReadAsync(new ReadAuthorityRangeV1(
                    session, cursor, snapshotThrough ?? long.MaxValue, maximumFacts, maximumEncodedBytes), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return Unknown("store-exception", accumulator.LastVerifiedPosition);
            }
            switch (result)
            {
                case ReadAuthorityRangeResultV1.StoreUnavailable unavailable:
                    return new SessionLifecycleSnapshotReadResultV1.OutcomeUnknown(
                        unavailable.SafeCode, accumulator.LastVerifiedPosition);
                case ReadAuthorityRangeResultV1.ItemTooLarge:
                    return Unknown("item-too-large", accumulator.LastVerifiedPosition);
                case ReadAuthorityRangeResultV1.Batch batch:
                    snapshotThrough ??= batch.SnapshotThrough;
                    if (batch.Session != session || batch.AfterExclusive != cursor || batch.SnapshotThrough != snapshotThrough)
                        return Unknown("snapshot-drift", accumulator.LastVerifiedPosition);
                    if (batch.Facts.Count > maximumFacts)
                        return Unknown("count-bound-violated", accumulator.LastVerifiedPosition);
                    foreach (var fact in batch.Facts)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        accumulator.Apply(fact);
                        cursor = fact.Position.Sequence;
                    }
                    if (!batch.HasMore)
                    {
                        if (cursor != snapshotThrough.Value)
                            return Unknown("incomplete-snapshot", accumulator.LastVerifiedPosition);
                        return new SessionLifecycleSnapshotReadResultV1.Verified(accumulator.Complete(), snapshotThrough.Value);
                    }
                    if (batch.Facts.Count == 0)
                        return Unknown("empty-continuation", accumulator.LastVerifiedPosition);
                    break;
                default:
                    return Unknown("unknown-read-result", accumulator.LastVerifiedPosition);
            }
        }
    }

    private static SessionLifecycleSnapshotReadResultV1.OutcomeUnknown Unknown(string code, long position) =>
        new(new BoundedAscii(code), position);
}

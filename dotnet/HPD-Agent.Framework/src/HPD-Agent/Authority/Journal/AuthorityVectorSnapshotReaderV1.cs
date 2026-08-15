namespace HPD.Agent.Authority;

internal abstract record AuthorityVectorSnapshotReadResultV1
{
    private AuthorityVectorSnapshotReadResultV1() { }

    internal sealed record Verified : AuthorityVectorSnapshotReadResultV1
    {
        internal Verified(AuthorityVectorReplayResultV1 replay, long snapshotThrough)
        {
            Replay = replay ?? throw new ArgumentNullException(nameof(replay));
            if (snapshotThrough < 0) throw new ArgumentOutOfRangeException(nameof(snapshotThrough));
            var replayThrough = replay switch
            {
                AuthorityVectorReplayResultV1.Current current => current.Snapshot.ThroughPosition,
                AuthorityVectorReplayResultV1.GenerationReplaced replaced => replaced.LastPosition,
                AuthorityVectorReplayResultV1.InvalidHistory invalid => invalid.LastPosition,
                _ => throw new ArgumentException("The replay result is outside the closed union.", nameof(replay)),
            };
            if (replay is not AuthorityVectorReplayResultV1.InvalidHistory && replayThrough != snapshotThrough || replayThrough > snapshotThrough)
                throw new ArgumentException("Replay must cover the complete pinned snapshot.", nameof(replay));
            SnapshotThrough = snapshotThrough;
        }

        internal AuthorityVectorReplayResultV1 Replay { get; }
        internal long SnapshotThrough { get; }
    }

    internal sealed record OutcomeUnknown : AuthorityVectorSnapshotReadResultV1
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

internal static class AuthorityVectorSnapshotReaderV1
{
    internal static async ValueTask<AuthorityVectorSnapshotReadResultV1> ReadAsync(
        IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session,
        ushort maximumFacts = AppendAuthorityBatchV1.MaximumItems,
        uint maximumEncodedBytes = ProposedAuthorityFactV1.MaximumPayloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!session.IsValid) throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        if (maximumFacts is 0 or > AppendAuthorityBatchV1.MaximumItems) throw new ArgumentOutOfRangeException(nameof(maximumFacts));
        if (maximumEncodedBytes is 0 or > ProposedAuthorityFactV1.MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
        var accumulator = AuthorityVectorReplayFoldV1.CreateAccumulator(session);
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
                    return new AuthorityVectorSnapshotReadResultV1.OutcomeUnknown(unavailable.SafeCode, accumulator.LastVerifiedPosition);
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
                        return new AuthorityVectorSnapshotReadResultV1.Verified(accumulator.Complete(), snapshotThrough.Value);
                    }
                    if (batch.Facts.Count == 0)
                        return Unknown("empty-continuation", accumulator.LastVerifiedPosition);
                    break;
                default:
                    return Unknown("unknown-read-result", accumulator.LastVerifiedPosition);
            }
        }
    }

    private static AuthorityVectorSnapshotReadResultV1.OutcomeUnknown Unknown(string code, long cursor) =>
        new(new BoundedAscii(code), cursor);
}

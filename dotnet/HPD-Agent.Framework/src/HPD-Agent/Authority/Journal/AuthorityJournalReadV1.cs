namespace HPD.Agent.Authority;

/// <summary>Defines one bounded, snapshot-pinned authority-journal range read.</summary>
public readonly record struct ReadAuthorityRangeV1
{
    /// <summary>Initializes a validated authority-journal range read.</summary>
    /// <param name="session">The authority session to inspect.</param>
    /// <param name="afterExclusive">The nonnegative position after which facts are returned.</param>
    /// <param name="throughInclusive">The positive requested upper position.</param>
    /// <param name="maximumFacts">The maximum number of returned facts, from one through 256.</param>
    /// <param name="maximumEncodedBytes">The maximum sum of canonical envelope bytes, from one through one MiB.</param>
    /// <exception cref="ArgumentException"><paramref name="session"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A cursor or bound is invalid.</exception>
    public ReadAuthorityRangeV1(
        SessionAuthorityStampV1 session,
        long afterExclusive,
        long throughInclusive,
        ushort maximumFacts,
        uint maximumEncodedBytes)
    {
        if (!session.IsValid) throw new ArgumentException("A valid authority session is required.", nameof(session));
        if (afterExclusive < 0) throw new ArgumentOutOfRangeException(nameof(afterExclusive));
        if (throughInclusive <= afterExclusive) throw new ArgumentOutOfRangeException(nameof(throughInclusive));
        if (maximumFacts is 0 or > AppendAuthorityBatchV1.MaximumItems) throw new ArgumentOutOfRangeException(nameof(maximumFacts));
        if (maximumEncodedBytes is 0 or > ProposedAuthorityFactV1.MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
        Session = session;
        AfterExclusive = afterExclusive;
        ThroughInclusive = throughInclusive;
        MaximumFacts = maximumFacts;
        MaximumEncodedBytes = maximumEncodedBytes;
    }

    /// <summary>Gets the authority session.</summary>
    public SessionAuthorityStampV1 Session { get; }
    /// <summary>Gets the exclusive lower journal position.</summary>
    public long AfterExclusive { get; }
    /// <summary>Gets the requested inclusive upper journal position.</summary>
    public long ThroughInclusive { get; }
    /// <summary>Gets the maximum returned fact count.</summary>
    public ushort MaximumFacts { get; }
    /// <summary>Gets the maximum returned canonical envelope bytes.</summary>
    public uint MaximumEncodedBytes { get; }
}

/// <summary>Describes one closed authority-journal range-read outcome.</summary>
public abstract record ReadAuthorityRangeResultV1
{
    private ReadAuthorityRangeResultV1() { }

    /// <summary>Returns a bounded prefix of one snapshot-pinned range.</summary>
    public sealed record Batch : ReadAuthorityRangeResultV1
    {
        private readonly AuthorityFactEnvelopeV1[] _facts;

        /// <summary>Initializes a validated bounded range result.</summary>
        /// <param name="session">The queried authority session.</param>
        /// <param name="snapshotHead">The observed session head, or zero when no session was observed.</param>
        /// <param name="afterExclusive">The request's nonnegative exclusive lower position.</param>
        /// <param name="snapshotThrough">The inclusive upper position pinned by this result.</param>
        /// <param name="facts">A contiguous ordered prefix after the request cursor.</param>
        /// <param name="hasMore">Whether more facts remain within <paramref name="snapshotThrough"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="facts"/> is null.</exception>
        /// <exception cref="ArgumentException">The session, range, or fact sequence is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A head is negative or the fact count exceeds 256.</exception>
        public Batch(
            SessionAuthorityStampV1 session,
            long snapshotHead,
            long afterExclusive,
            long snapshotThrough,
            IEnumerable<AuthorityFactEnvelopeV1> facts,
            bool hasMore)
        {
            if (!session.IsValid) throw new ArgumentException("A valid authority session is required.", nameof(session));
            if (snapshotHead < 0) throw new ArgumentOutOfRangeException(nameof(snapshotHead));
            if (afterExclusive < 0) throw new ArgumentOutOfRangeException(nameof(afterExclusive));
            if (snapshotThrough < 0 || snapshotThrough > snapshotHead) throw new ArgumentOutOfRangeException(nameof(snapshotThrough));
            ArgumentNullException.ThrowIfNull(facts);
            var owned = new List<AuthorityFactEnvelopeV1>();
            foreach (var fact in facts)
            {
                if (owned.Count == AppendAuthorityBatchV1.MaximumItems) throw new ArgumentOutOfRangeException(nameof(facts));
                owned.Add(fact);
            }
            _facts = owned.ToArray();
            if (_facts.Length > 0 && afterExclusive == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(afterExclusive));
            if (_facts.Any(fact => fact is null || fact.Position.Session != session || fact.Position.Sequence > snapshotThrough) ||
                _facts.Zip(_facts.Skip(1), static (left, right) => right.Position.Sequence != left.Position.Sequence + 1).Any(static gap => gap))
                throw new ArgumentException("Facts must be a contiguous ordered prefix in the queried session and snapshot.", nameof(facts));
            if ((_facts.Length > 0 && _facts[0].Position.Sequence != checked(afterExclusive + 1)) ||
                (_facts.Length == 0 && afterExclusive < snapshotThrough) ||
                hasMore != (_facts.Length > 0 && _facts[^1].Position.Sequence < snapshotThrough))
                throw new ArgumentException("The batch must exactly describe a contiguous prefix and continuation state.", nameof(facts));
            Session = session;
            SnapshotHead = snapshotHead;
            AfterExclusive = afterExclusive;
            SnapshotThrough = snapshotThrough;
            Facts = Array.AsReadOnly(_facts);
            HasMore = hasMore;
        }

        /// <summary>Gets the queried authority session.</summary>
        public SessionAuthorityStampV1 Session { get; }
        /// <summary>Gets the session head observed atomically with the range.</summary>
        public long SnapshotHead { get; }
        /// <summary>Gets the exclusive lower position used for this batch.</summary>
        public long AfterExclusive { get; }
        /// <summary>Gets the pinned inclusive upper position.</summary>
        public long SnapshotThrough { get; }
        /// <summary>Gets the contiguous ordered fact prefix.</summary>
        public IReadOnlyList<AuthorityFactEnvelopeV1> Facts { get; }
        /// <summary>Gets whether more facts remain within the pinned upper position.</summary>
        public bool HasMore { get; }
    }

    /// <summary>Reports that the next indivisible envelope exceeds the caller's byte bound.</summary>
    public sealed record ItemTooLarge : ReadAuthorityRangeResultV1
    {
        /// <summary>Initializes an indivisible-item refusal.</summary>
        /// <param name="position">The exact unread position.</param>
        /// <param name="requiredBytes">The positive canonical envelope byte count.</param>
        /// <param name="maximumBytes">The smaller positive caller bound.</param>
        /// <exception cref="ArgumentException"><paramref name="position"/> is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The byte relation is invalid.</exception>
        public ItemTooLarge(JournalPositionV1 position, ulong requiredBytes, uint maximumBytes)
        {
            if (!position.IsValid) throw new ArgumentException("A valid unread position is required.", nameof(position));
            if (maximumBytes == 0 || requiredBytes <= maximumBytes) throw new ArgumentOutOfRangeException(nameof(requiredBytes));
            Position = position;
            RequiredBytes = requiredBytes;
            MaximumBytes = maximumBytes;
        }

        /// <summary>Gets the exact unread position.</summary>
        public JournalPositionV1 Position { get; }
        /// <summary>Gets the canonical bytes required for the indivisible envelope.</summary>
        public ulong RequiredBytes { get; }
        /// <summary>Gets the caller's smaller byte bound.</summary>
        public uint MaximumBytes { get; }
    }

    /// <summary>Reports a read failure that proves neither presence nor absence.</summary>
    public sealed record StoreUnavailable : ReadAuthorityRangeResultV1
    {
        /// <summary>Initializes a store-unavailable result.</summary>
        /// <param name="safeCode">A bounded nonsecret diagnostic code.</param>
        /// <exception cref="ArgumentException"><paramref name="safeCode"/> is invalid.</exception>
        public StoreUnavailable(BoundedAscii safeCode) =>
            SafeCode = safeCode.IsValid ? safeCode : throw new ArgumentException("A safe code is required.", nameof(safeCode));

        /// <summary>Gets the bounded nonsecret diagnostic code.</summary>
        public BoundedAscii SafeCode { get; }
    }
}

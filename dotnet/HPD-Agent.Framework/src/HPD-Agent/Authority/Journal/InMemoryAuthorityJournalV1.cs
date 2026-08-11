namespace HPD.Agent.Authority;

internal readonly record struct AuthorityJournalCapacityV1
{
    internal AuthorityJournalCapacityV1(int maximumSessions, int maximumFacts, ulong maximumResidentBytes)
    {
        if (maximumSessions <= 0) throw new ArgumentOutOfRangeException(nameof(maximumSessions));
        if (maximumFacts < maximumSessions) throw new ArgumentOutOfRangeException(nameof(maximumFacts));
        if (maximumResidentBytes == 0) throw new ArgumentOutOfRangeException(nameof(maximumResidentBytes));
        MaximumSessions = maximumSessions;
        MaximumFacts = maximumFacts;
        MaximumResidentBytes = maximumResidentBytes;
    }

    internal int MaximumSessions { get; }
    internal int MaximumFacts { get; }
    internal ulong MaximumResidentBytes { get; }
}

internal sealed class InMemoryAuthorityJournalV1 : IAuthorityJournalV1
{
    private readonly object _gate = new();
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly Func<UtcInstant> _clock;
    private readonly AuthorityJournalCapacityV1 _capacity;
    private readonly Dictionary<SessionAuthorityStampV1, SessionState> _sessions = [];
    private readonly Dictionary<JournalFactId, StoredFact> _facts = [];
    private ulong _residentBytes;

    internal InMemoryAuthorityJournalV1(
        AuthorityPayloadAdmissionRegistryV1 registry,
        Func<UtcInstant> clock,
        AuthorityJournalCapacityV1 capacity)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _capacity = capacity;
        if (_capacity.MaximumSessions <= 0) throw new ArgumentException("A valid resident capacity is required.", nameof(capacity));
    }

    public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = Validate(request);
        if (validation is not null) return ValueTask.FromResult(validation);

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(AppendLocked(request));
        }
    }

    private AppendAuthorityResultV1? Validate(AppendAuthorityBatchV1 request)
    {
        foreach (var fact in request.Facts)
        {
            var disposition = _registry.Validate(fact, out _);
            if (disposition == AuthorityPayloadAdmissionV1.UnknownSchema)
                return new AppendAuthorityResultV1.UnknownSchema(fact.PayloadSchema);
            if (disposition != AuthorityPayloadAdmissionV1.Exact)
                return new AppendAuthorityResultV1.InvalidPayload(new BoundedAscii(disposition switch
                {
                    AuthorityPayloadAdmissionV1.OwnerMismatch => "owner-mismatch",
                    AuthorityPayloadAdmissionV1.HashMismatch => "hash-mismatch",
                    _ => "invalid-canonical-payload",
                }));
        }
        var encodedLength = AuthorityCanonicalCborV1.GetAppendBatchEncodedLength(request);
        if (encodedLength > request.MaximumEncodedBytes)
            return new AppendAuthorityResultV1.CapacityRefused(CapacityDimensionId.JournalBytes, encodedLength, request.MaximumEncodedBytes);
        var encoded = AuthorityCanonicalCborV1.EncodeAppendBatch(request);
        if ((ulong)encoded.Length != encodedLength)
            throw new InvalidOperationException("The canonical batch length calculation diverged from the registered encoder.");
        return null;
    }

    private AppendAuthorityResultV1 AppendLocked(AppendAuthorityBatchV1 request)
    {
        var existing = request.Facts.Select(fact => _facts.TryGetValue(fact.FactId, out var stored) ? stored : null).ToArray();
        if (existing.Any(static item => item is not null))
        {
            if (existing.Any(static item => item is null))
                return new AppendAuthorityResultV1.InvalidPayload(new BoundedAscii("mixed-idempotency-batch"));
            for (var index = 0; index < request.Facts.Count; index++)
            {
                var stored = existing[index];
                var proposal = request.Facts[index];
                if (!stored!.Matches(request.Session, proposal))
                    return new AppendAuthorityResultV1.ContradictoryDuplicate(
                        proposal.FactId, stored?.Envelope.PayloadHash ?? proposal.PayloadHash, proposal.PayloadHash);
            }
            return new AppendAuthorityResultV1.AlreadyCommitted(existing.Select(static item => item!.Envelope));
        }

        var isNewSession = !_sessions.TryGetValue(request.Session, out var state);
        state ??= new SessionState();
        if (state.Head != request.ExpectedSessionHead)
            return new AppendAuthorityResultV1.SessionConflict(request.ExpectedSessionHead, state.Head);
        foreach (var expected in request.ExpectedThreadHeads)
        {
            if (state.Threads.TryGetValue(expected.ThreadId, out var actual))
            {
                if (actual.Generation != expected.Generation || actual.Sequence != expected.Sequence)
                    return new AppendAuthorityResultV1.ThreadConflict(expected.ThreadId, expected.Sequence, actual.Sequence);
            }
            else if (expected.Sequence != 0)
                return new AppendAuthorityResultV1.ThreadConflict(expected.ThreadId, expected.Sequence, 0);
        }

        var nextHead = state.Head;
        var nextThreads = state.Threads.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        var envelopes = new List<AuthorityFactEnvelopeV1>(request.Facts.Count);
        ulong batchResidentBytes = 0;
        var admittedAt = _clock();
        foreach (var proposal in request.Facts)
        {
            var position = new JournalPositionV1(request.Session, ++nextHead);
            ThreadPositionV1? threadPosition = null;
            if (proposal.ThreadId is { } threadId)
            {
                var expected = request.ExpectedThreadHeads.Single(head => head.ThreadId == threadId);
                var current = nextThreads.TryGetValue(threadId, out var value) ? value : new ThreadHead(expected.Generation, 0);
                current = new ThreadHead(current.Generation, current.Sequence + 1);
                nextThreads[threadId] = current;
                threadPosition = new ThreadPositionV1(threadId, current.Generation, current.Sequence);
            }
            var preimage = AuthorityCanonicalCborV1.EncodeEnvelopeWithoutIntegrity(proposal, position, threadPosition, admittedAt);
            var integrity = new IntegrityEnvelopeV1(1, 1,
                AuthorityIntegrityHashV1.Compute("hpd.authority-fact-envelope.v1", 1, 0, preimage), []);
            batchResidentBytes = checked(batchResidentBytes + AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(preimage, integrity));
            envelopes.Add(new AuthorityFactEnvelopeV1(
                proposal.FactId, position, threadPosition, proposal.Owner, proposal.PayloadSchema, proposal.PayloadBytes,
                proposal.PayloadHash, proposal.Correlation, proposal.ObservedAt, admittedAt, integrity));
        }


        if (isNewSession && _sessions.Count >= _capacity.MaximumSessions)
            return new AppendAuthorityResultV1.CapacityRefused(CapacityDimensionId.QueueItems, 1, 0);
        var availableFacts = _capacity.MaximumFacts - _facts.Count;
        if (request.Facts.Count > availableFacts)
            return new AppendAuthorityResultV1.CapacityRefused(
                CapacityDimensionId.QueueItems, (ulong)request.Facts.Count, (ulong)Math.Max(availableFacts, 0));
        var availableBytes = _capacity.MaximumResidentBytes - Math.Min(_capacity.MaximumResidentBytes, _residentBytes);
        if (batchResidentBytes > availableBytes)
            return new AppendAuthorityResultV1.CapacityRefused(CapacityDimensionId.JournalBytes, batchResidentBytes, availableBytes);

        var previousHead = state.Head;
        state.Head = nextHead;
        state.Threads.Clear();
        foreach (var pair in nextThreads) state.Threads.Add(pair.Key, pair.Value);
        if (isNewSession) _sessions.Add(request.Session, state);
        foreach (var envelope in envelopes) _facts.Add(envelope.FactId, new StoredFact(request.Session, envelope));
        _residentBytes = checked(_residentBytes + batchResidentBytes);
        return new AppendAuthorityResultV1.Committed(previousHead, nextHead, envelopes);
    }

    private sealed class SessionState
    {
        internal long Head { get; set; }
        internal Dictionary<ThreadId, ThreadHead> Threads { get; } = [];
    }

    private sealed record StoredFact(SessionAuthorityStampV1 Session, AuthorityFactEnvelopeV1 Envelope)
    {
        internal bool Matches(SessionAuthorityStampV1 session, ProposedAuthorityFactV1 proposal) =>
            Session == session && Envelope.PayloadHash == proposal.PayloadHash && Envelope.ThreadScope?.ThreadId == proposal.ThreadId &&
            Envelope.Owner == proposal.Owner && Envelope.PayloadSchema == proposal.PayloadSchema;
    }

    private readonly record struct ThreadHead(long Generation, long Sequence);
}

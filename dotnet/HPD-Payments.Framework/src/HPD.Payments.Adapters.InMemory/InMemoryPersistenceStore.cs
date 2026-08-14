using System.Buffers.Binary;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;

namespace HPD.Payments.Adapters.InMemory;

/// <summary>Provides a deterministic, process-local reference implementation of the frozen E-LOCAL persistence behavior.</summary>
/// <remarks>This type is a behavioral oracle and test adapter. It makes no durability, cross-process, or real-store claim.</remarks>
public sealed class InMemoryPersistenceStore : IRelationPersistencePort, IContinuationPersistencePort, ICustodyPersistencePort
{
    private readonly object _gate = new();
    private Dictionary<OwnerKey, OwnerState> _owners = [];
    private Dictionary<SemanticId, SupportingRelation> _relations = [];
    private Dictionary<SemanticId, ContinuationDeclaration> _continuations = [];
    private Dictionary<SemanticId, CustodyInstance> _custody = [];
    private bool _alive = true;

    /// <summary>Creates a typed owner-local port sharing this store's single isolation boundary.</summary>
    /// <typeparam name="TFact">Immutable authority fact type supplied and interpreted by the caller.</typeparam>
    /// <returns>A typed port over this store.</returns>
    public IOwnerPersistencePort<TFact> CreateOwnerPort<TFact>() where TFact : notnull => new OwnerPort<TFact>(this);

    /// <summary>Captures an owned point-in-time image suitable for deterministic death-and-restore tests.</summary>
    /// <returns>An immutable store image with independent collection ownership.</returns>
    public InMemoryStoreSnapshot CaptureSnapshot()
    {
        lock (_gate)
        {
            EnsureAlive();
            return new(new SnapshotState(CloneOwners(_owners), new(_relations), new(_continuations), new(_custody)));
        }
    }

    /// <summary>Simulates abrupt process death by discarding all live state and rejecting operations until restore.</summary>
    public void SimulateDeath()
    {
        lock (_gate)
        {
            _owners.Clear(); _relations.Clear(); _continuations.Clear(); _custody.Clear(); _alive = false;
        }
    }

    /// <summary>Restores an owned snapshot and makes the deterministic reference store live again.</summary>
    /// <param name="snapshot">Previously captured image.</param>
    /// <exception cref="ArgumentNullException">The snapshot is null.</exception>
    public void Restore(InMemoryStoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var state = (SnapshotState)snapshot.State;
            _owners = CloneOwners(state.Owners); _relations = new(state.Relations);
            _continuations = new(state.Continuations); _custody = new(state.Custody); _alive = true;
        }
    }

    /// <summary>Removes custody instances already recorded as verified absent through the supplied inventory generation.</summary>
    /// <param name="throughGeneration">Inclusive inventory-generation sweep cut.</param>
    /// <returns>The number of per-instance observations removed; no authority fact is removed.</returns>
    public int SweepVerifiedAbsent(OwnerGeneration throughGeneration)
    {
        if (!throughGeneration.IsValid) throw new ArgumentException("A valid sweep generation is required.", nameof(throughGeneration));
        lock (_gate)
        {
            EnsureAlive();
            var keys = _custody.Where(x => x.Value.State == CustodyState.VerifiedAbsent && x.Value.InventoryGeneration.Value <= throughGeneration.Value).Select(x => x.Key).ToArray();
            foreach (var key in keys) _custody.Remove(key);
            return keys.Length;
        }
    }

    /// <inheritdoc />
    public ValueTask<PersistenceReceipt> GuardedRelateAsync(SupportingRelation relation, AtomicDomain domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relation); cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            EnsureAlive();
            if (!IsLocal(domain) || relation.RelationId.Scope != domain.DomainId.Scope)
                return ValueTask.FromResult(Receipt(relation.RelationId, domain, "guarded-relate", PersistenceObservation.Unsupported, "e-local-only"));
            if (!HasGeneration(relation.Source) || !HasGeneration(relation.Target))
                return ValueTask.FromResult(Receipt(relation.RelationId, domain, "guarded-relate", PersistenceObservation.Failed, "endpoint-generation-conflict"));
            if (_relations.TryGetValue(relation.RelationId, out var existing) && existing != relation)
                return ValueTask.FromResult(Receipt(relation.RelationId, domain, "guarded-relate", PersistenceObservation.Failed, "relation-conflict"));
            _relations[relation.RelationId] = relation;
            return ValueTask.FromResult(Receipt(relation.RelationId, domain, "guarded-relate", PersistenceObservation.Observed, "none"));
        }
    }

    /// <inheritdoc />
    public ValueTask<PersistenceReceipt> CommitDiscoverableAsync(ContinuationDeclaration continuation, AtomicDomain domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(continuation); cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            EnsureAlive();
            if (!IsLocal(domain) || continuation.ContinuationId.Scope != domain.DomainId.Scope)
                return ValueTask.FromResult(Receipt(continuation.ContinuationId, domain, "commit-continuation", PersistenceObservation.Unsupported, "e-local-only"));
            if (!HasGeneration(continuation.Owner))
                return ValueTask.FromResult(Receipt(continuation.ContinuationId, domain, "commit-continuation", PersistenceObservation.Failed, "owner-generation-conflict"));
            if (_continuations.TryGetValue(continuation.ContinuationId, out var existing) && existing != continuation)
                return ValueTask.FromResult(Receipt(continuation.ContinuationId, domain, "commit-continuation", PersistenceObservation.Failed, "continuation-conflict"));
            _continuations[continuation.ContinuationId] = continuation;
            return ValueTask.FromResult(Receipt(continuation.ContinuationId, domain, "commit-continuation", PersistenceObservation.Observed, "none"));
        }
    }

    /// <inheritdoc />
    public ValueTask<ContinuationDiscoveryPage> DiscoverAsync(AtomicDomain domain, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumItems is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        lock (_gate)
        {
            EnsureAlive(); RequireLocal(domain);
            var ordered = _continuations.Values.Where(x => x.ContinuationId.Scope == domain.DomainId.Scope).OrderBy(x => Convert.ToHexString(x.ContinuationId.GetCanonicalBytes()), StringComparer.Ordinal).ToArray();
            var offset = DecodeToken(continuation.Span, maximumItems, 0);
            if (offset > ordered.Length) throw new ArgumentException("Continuation is outside the current result set.", nameof(continuation));
            var items = ordered.Skip(offset).Take(maximumItems).ToArray();
            var next = offset + items.Length < ordered.Length ? EncodeToken(offset + items.Length, maximumItems, 0) : [];
            return ValueTask.FromResult(new ContinuationDiscoveryPage(items, next));
        }
    }

    /// <inheritdoc />
    public ValueTask<PersistenceReceipt> RecordCustodyAsync(CustodyInstance custody, AtomicDomain domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(custody); cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            EnsureAlive();
            if (!IsLocal(domain) || custody.InstanceId.Scope != domain.DomainId.Scope)
                return ValueTask.FromResult(Receipt(custody.InstanceId, domain, "record-custody", PersistenceObservation.Unsupported, "e-local-only"));
            if (!HasGeneration(custody.Subject))
                return ValueTask.FromResult(Receipt(custody.InstanceId, domain, "record-custody", PersistenceObservation.Failed, "owner-generation-conflict"));
            if (_custody.TryGetValue(custody.InstanceId, out var existing) && existing.InventoryGeneration.Value > custody.InventoryGeneration.Value)
                return ValueTask.FromResult(Receipt(custody.InstanceId, domain, "record-custody", PersistenceObservation.Failed, "custody-generation-regression"));
            _custody[custody.InstanceId] = custody;
            return ValueTask.FromResult(Receipt(custody.InstanceId, domain, "record-custody", PersistenceObservation.Observed, custody.State == CustodyState.Residual ? "residue-retained" : "none"));
        }
    }

    /// <inheritdoc />
    public ValueTask<CustodyPage> ReadCustodyAsync(OwnerReference owner, OwnerGeneration throughGeneration, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.IsValid || !throughGeneration.IsValid || maximumItems is < 1 or > 1024) throw new ArgumentException("A valid owner, generation, and bound are required.");
        lock (_gate)
        {
            EnsureAlive();
            var ordered = _custody.Values.Where(x => x.Subject.SubjectId == owner.SubjectId && x.InventoryGeneration.Value <= throughGeneration.Value).OrderBy(x => Convert.ToHexString(x.InstanceId.GetCanonicalBytes()), StringComparer.Ordinal).ToArray();
            var offset = DecodeToken(continuation.Span, maximumItems, throughGeneration.Value);
            if (offset > ordered.Length) throw new ArgumentException("Continuation is outside the current result set.", nameof(continuation));
            var items = ordered.Skip(offset).Take(maximumItems).ToArray();
            var next = offset + items.Length < ordered.Length ? EncodeToken(offset + items.Length, maximumItems, throughGeneration.Value) : [];
            return ValueTask.FromResult(new CustodyPage(items, next));
        }
    }

    private OwnerAppendReceipt<TFact> Append<TFact>(OwnerAppendRequest<TFact> request) where TFact : notnull
    {
        lock (_gate)
        {
            EnsureAlive();
            if (!IsLocal(request.Domain)) return new(request.ExpectedOwner, OwnerAppendDisposition.Unsupported, request.ExpectedOwner.Generation, default, "e-local-only");
            var key = new OwnerKey(request.ExpectedOwner.Authority, request.ExpectedOwner.SubjectId);
            if (!_owners.TryGetValue(key, out var state))
            {
                state = new(request.ExpectedOwner.Generation, typeof(TFact)); _owners.Add(key, state);
            }
            else if (state.FactType != typeof(TFact)) return new(request.ExpectedOwner, OwnerAppendDisposition.Rejected, state.Generation, default, "fact-type-conflict");
            var replay = state.Entries.FirstOrDefault(x => x.Digest.Equals(request.SemanticDigest));
            if (replay is not null) return new(new(request.ExpectedOwner.Authority, request.ExpectedOwner.SubjectId, replay.Generation), OwnerAppendDisposition.Replay, replay.Generation, (TFact)replay.Fact, "replay");
            if (state.Generation != request.ExpectedOwner.Generation)
                return new(new(request.ExpectedOwner.Authority, request.ExpectedOwner.SubjectId, state.Generation), OwnerAppendDisposition.Conflict, state.Generation, default, "generation-conflict");
            var generation = state.Generation.TryNext(out var next) ? next : default;
            if (!generation.IsValid) return new(request.ExpectedOwner, OwnerAppendDisposition.Rejected, state.Generation, default, "generation-exhausted");
            state.Generation = generation; state.Entries.Add(new(request.SemanticDigest, request.Fact, generation));
            return new(new(request.ExpectedOwner.Authority, request.ExpectedOwner.SubjectId, generation), OwnerAppendDisposition.Appended, generation, request.Fact, "appended");
        }
    }

    private OwnerHistoryPage<TFact> History<TFact>(OwnerHistoryRequest request, ReadOnlyMemory<byte> continuation) where TFact : notnull
    {
        lock (_gate)
        {
            EnsureAlive();
            var key = new OwnerKey(request.Owner.Authority, request.Owner.SubjectId);
            if (!_owners.TryGetValue(key, out var state) || state.FactType != typeof(TFact)) throw new KeyNotFoundException("No history exists for the exact owner and fact type.");
            var cut = request.Frame.OwnerCuts.Single(x => x.Owner.SubjectId == request.Owner.SubjectId).Owner.Generation.Value;
            var facts = state.Entries.Where(x => x.Generation.Value <= cut).ToArray();
            var offset = DecodeToken(continuation.Span, request.MaximumFacts, cut);
            if (offset >= facts.Length) throw new ArgumentException("Continuation is outside the historical result set.", nameof(continuation));
            var selected = facts.Skip(offset).Take(request.MaximumFacts).ToArray();
            var next = offset + selected.Length < facts.Length ? EncodeToken(offset + selected.Length, request.MaximumFacts, cut) : [];
            return new(selected.Select(x => (TFact)x.Fact).ToArray(), selected[^1].Generation, next);
        }
    }

    private bool HasGeneration(OwnerReference owner) => _owners.TryGetValue(new(owner.Authority, owner.SubjectId), out var state) && state.Generation == owner.Generation;
    private static bool IsLocal(AtomicDomain domain) => domain.IsValid && domain.Kind == AtomicDomainKind.Local;
    private static void RequireLocal(AtomicDomain domain) { if (!IsLocal(domain)) throw new NotSupportedException("The InMemory reference adapter supports only E-LOCAL."); }
    private void EnsureAlive() { if (!_alive) throw new InvalidOperationException("The simulated process is dead; restore a snapshot before use."); }
    private static PersistenceReceipt Receipt(SemanticId id, AtomicDomain domain, string operation, PersistenceObservation observation, string limitation) =>
        new(id, observation, new(domain, operation, observation, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch), Evidence(), limitation));
    private static CanonicalDigest Evidence() => CanonicalDigest.Sha256(new("inmemory", ContractVersion.Create(1, 0), "state", "ordinal", "utc", "canonical", "builtin"), "hpd-payments-inmemory-e-local"u8);
    private static byte[] EncodeToken(int offset, int size, ulong cut) { var token = new byte[16]; BinaryPrimitives.WriteInt32BigEndian(token, offset); BinaryPrimitives.WriteInt32BigEndian(token.AsSpan(4), size); BinaryPrimitives.WriteUInt64BigEndian(token.AsSpan(8), cut); return token; }
    private static int DecodeToken(ReadOnlySpan<byte> token, int size, ulong cut)
    {
        if (token.IsEmpty) return 0;
        if (token.Length != 16 || BinaryPrimitives.ReadInt32BigEndian(token[4..]) != size || BinaryPrimitives.ReadUInt64BigEndian(token[8..]) != cut) throw new ArgumentException("Continuation does not belong to this exact request shape.");
        var offset = BinaryPrimitives.ReadInt32BigEndian(token); if (offset < 0) throw new ArgumentException("Continuation offset is invalid."); return offset;
    }
    private static Dictionary<OwnerKey, OwnerState> CloneOwners(Dictionary<OwnerKey, OwnerState> source) => source.ToDictionary(x => x.Key, x => x.Value.Clone());

    private sealed class OwnerPort<TFact>(InMemoryPersistenceStore store) : IOwnerPersistencePort<TFact> where TFact : notnull
    {
        public ValueTask<OwnerAppendReceipt<TFact>> CompareBindAppendAsync(OwnerAppendRequest<TFact> request, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(store.Append(request)); }
        public ValueTask<OwnerHistoryPage<TFact>> ReadHistoryAsync(OwnerHistoryRequest request, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(store.History<TFact>(request, continuation)); }
    }
    internal readonly record struct OwnerKey(FrozenAuthority Authority, SemanticId Subject);
    internal sealed class OwnerState(OwnerGeneration generation, Type factType)
    {
        public OwnerGeneration Generation = generation; public Type FactType { get; } = factType; public List<Entry> Entries { get; } = [];
        public OwnerState Clone() { var clone = new OwnerState(Generation, FactType); clone.Entries.AddRange(Entries); return clone; }
    }
    internal sealed record Entry(CanonicalDigest Digest, object Fact, OwnerGeneration Generation);
    private sealed record SnapshotState(Dictionary<OwnerKey, OwnerState> Owners, Dictionary<SemanticId, SupportingRelation> Relations, Dictionary<SemanticId, ContinuationDeclaration> Continuations, Dictionary<SemanticId, CustodyInstance> Custody);
}

/// <summary>Owns a complete deterministic store image; restoration always takes defensive collection copies.</summary>
public sealed class InMemoryStoreSnapshot
{
    internal object State { get; }
    internal InMemoryStoreSnapshot(object state) => State = state;
}

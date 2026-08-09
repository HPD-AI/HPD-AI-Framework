using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace HPD.Base;

internal interface IInMemoryProjectionAuthority
{
    ValueTask<OperationResult<IInMemoryProjectionReadSession>> CaptureAsync(CancellationToken cancellationToken);
    ValueTask<OperationResult<IInMemoryProjectionReplacement>> BeginReplacementAsync(
        long expectedRootGeneration,
        long expectedProjectionGeneration,
        CancellationToken cancellationToken);
}

internal interface IInMemoryProjectionReadSession : IBaseVectorHydrationSession
{
    BaseInMemoryProjectionSnapshot ProjectionSnapshot { get; }
    BaseInMemoryProjectionStateReader State { get; }
    ValueTask<OperationResult<BaseInMemoryProjectionSourcePage>> EnumerateProjectionSourceAsync(
        BaseInMemoryProjectionIndexHandle index,
        BaseInMemoryProjectionSourceScanRequest request,
        CancellationToken cancellationToken);
}

internal interface IInMemoryProjectionReplacement : IAsyncDisposable
{
    BaseInMemoryProjectionStateWriter Writer { get; }
    ValueTask<OperationResult<BaseInMemoryProjectionReplacementOutcome>> PublishAsync(CancellationToken cancellationToken);
}

internal enum BaseInMemoryProjectionReplacementOutcome
{
    Published,
    RootGenerationChanged,
    ProjectionGenerationChanged,
    CapacityExceeded,
    InvalidState,
    SessionClosed,
}

internal sealed class BaseInMemoryProjectionSnapshot
{
    private readonly BaseInMemoryProjectionIndexHandle[] _handles;
    private readonly ReadOnlyDictionary<string, long> _purgeGenerations;
    private readonly ReadOnlyDictionary<string, int> _recordCounts;

    internal BaseInMemoryProjectionSnapshot(InMemoryStoreState root, long rootGeneration, string storeIdentity, CollectionDefinition[] collections)
    {
        StoreIdentityDigest = new string(storeIdentity.AsSpan());
        RootGeneration = rootGeneration;
        GlobalMutationHighWater = root.GlobalMutationPosition;
        ContributorId = InMemoryVectorMutationProjection.ContributorId;
        SchemaDigest = HPDBaseStoreInstallationContext.ComputeSchemaDigest(collections);
        _purgeGenerations = new ReadOnlyDictionary<string, long>(collections.ToDictionary(
            static collection => collection.Id,
            collection => root.Collections.GetValueOrDefault(collection.Id)?.PurgeGeneration ?? 0,
            StringComparer.Ordinal));
        _recordCounts = new ReadOnlyDictionary<string, int>(collections.ToDictionary(
            static collection => collection.Id,
            collection => root.Collections.GetValueOrDefault(collection.Id)?.RecordsById.Count ?? 0,
            StringComparer.Ordinal));
        _handles = collections
            .SelectMany(collection => (collection.VectorIndexes ?? []).Select(index => new BaseInMemoryProjectionIndexHandle(
                this,
                collection,
                index,
                root.VectorProjections.GetValueOrDefault(collection.Id + "\n" + index.Id)?.Generation ?? 1,
                root.Collections.GetValueOrDefault(collection.Id)?.PurgeGeneration ?? 0)))
            .OrderBy(static handle => handle.Collection.Id, StringComparer.Ordinal)
            .ThenBy(static handle => handle.Index.Id, StringComparer.Ordinal)
            .ToArray();
    }

    internal string StoreIdentityDigest { get; }
    internal long RestoreEpoch => 0;
    internal long SchemaGeneration => 1;
    internal long RootGeneration { get; }
    internal string ContributorId { get; }
    internal string SchemaDigest { get; }
    internal long GlobalMutationHighWater { get; }
    internal IReadOnlyDictionary<string, long> PurgeGenerations => _purgeGenerations;
    internal IReadOnlyDictionary<string, int> AuthoritativeRecordCounts => _recordCounts;
    internal BaseInMemoryProjectionIndexHandle[] GetIndexHandles() => _handles.ToArray();
}

internal sealed class BaseInMemoryProjectionIndexHandle
{
    internal BaseInMemoryProjectionIndexHandle(BaseInMemoryProjectionSnapshot owner, CollectionDefinition collection, VectorIndexDefinition index, long generation, long purgeGeneration)
    { Owner = owner; Collection = collection; Index = index; Generation = generation; PurgeGeneration = purgeGeneration; }
    internal BaseInMemoryProjectionSnapshot Owner { get; }
    internal CollectionDefinition Collection { get; }
    internal VectorIndexDefinition Index { get; }
    internal long Generation { get; }
    internal long PurgeGeneration { get; }
}

internal sealed class BaseInMemoryProjectionStateReader
{
    private readonly InMemoryProjectionReadSession _session;
    internal BaseInMemoryProjectionStateReader(InMemoryProjectionReadSession session) => _session = session;
    internal IReadOnlyDictionary<string, InMemoryVectorCarrier> GetCarriers(BaseInMemoryProjectionIndexHandle handle)
    {
        _session.ThrowIfClosed();
        _session.ValidateHandle(handle);
        Dictionary<string, InMemoryVectorCarrier>? carriers = _session.Root.VectorProjections.GetValueOrDefault(handle.Collection.Id + "\n" + handle.Index.Id)?.Carriers;
        return carriers is null
            ? InMemoryProjectionReadSession.EmptyCarriers
            : new ReadOnlyDictionary<string, InMemoryVectorCarrier>(carriers);
    }
}

internal sealed class BaseInMemoryProjectionSourceScanRequest
{
    internal BaseInMemoryProjectionSourceScanRequest(int? sourceEntryLimit = null, BaseInMemoryProjectionSourceCursor? cursor = null)
    {
        if (sourceEntryLimit is < 1 or > 1_024) throw new ArgumentOutOfRangeException(nameof(sourceEntryLimit));
        SourceEntryLimit = sourceEntryLimit;
        Cursor = cursor;
    }
    internal int? SourceEntryLimit { get; }
    internal BaseInMemoryProjectionSourceCursor? Cursor { get; }
}

internal sealed class BaseInMemoryProjectionSourceCursor
{
    internal BaseInMemoryProjectionSourceCursor(InMemoryProjectionReadSession owner, BaseInMemoryProjectionIndexHandle handle, IEnumerator<string> enumerator, string nextRecordId)
    { Owner = owner; Handle = handle; Enumerator = enumerator; NextRecordId = nextRecordId; }
    internal InMemoryProjectionReadSession Owner { get; }
    internal BaseInMemoryProjectionIndexHandle Handle { get; }
    internal IEnumerator<string> Enumerator { get; }
    internal string? NextRecordId { get; set; }
    internal bool Consumed { get; set; }
}

internal sealed class BaseInMemoryProjectionSourceRecord
{
    internal BaseInMemoryProjectionSourceRecord(RecordId id, RevisionToken revision, long position, BaseVector vector, BaseInMemoryProjectionFilterSlot[] filters)
    { RecordId = id; Revision = revision; LatestMutationPosition = position; Vector = BaseVector.Create(vector.ToArray()); Filters = filters.Select(static value => value.Copy()).ToArray(); }
    internal RecordId RecordId { get; }
    internal RevisionToken Revision { get; }
    internal long LatestMutationPosition { get; }
    internal BaseVector Vector { get; }
    internal BaseInMemoryProjectionFilterSlot[] Filters { get; }
}

internal sealed class BaseInMemoryProjectionFilterSlot
{
    private BaseInMemoryProjectionFilterSlot(string fieldId, bool missing, BaseVectorFilterValue? value)
    { FieldId = new string(fieldId.AsSpan()); Missing = missing; Value = value; }
    internal string FieldId { get; }
    internal bool Missing { get; }
    internal BaseVectorFilterValue? Value { get; }
    internal static BaseInMemoryProjectionFilterSlot MissingValue(string fieldId) => new(fieldId, true, null);
    internal static BaseInMemoryProjectionFilterSlot Present(string fieldId, BaseVectorFilterValue value) => new(fieldId, false, value);
    internal BaseInMemoryProjectionFilterSlot Copy() => new(FieldId, Missing, Value);
}

internal sealed class BaseInMemoryProjectionSourcePage
{
    internal BaseInMemoryProjectionSourcePage(BaseInMemoryProjectionSourceRecord[] records, int examined, BaseInMemoryProjectionSourceCursor? cursor)
    { Records = records.ToArray(); ExaminedSourceEntries = examined; Cursor = cursor; }
    internal BaseInMemoryProjectionSourceRecord[] Records { get; }
    internal int ExaminedSourceEntries { get; }
    internal BaseInMemoryProjectionSourceCursor? Cursor { get; }
}

internal sealed class BaseInMemoryProjectionStateWriter
{
    private readonly InMemoryProjectionReplacement _owner;
    private readonly Dictionary<string, InMemoryVectorCarrier> _carriers = new(StringComparer.Ordinal);
    private BaseInMemoryProjectionIndexHandle? _handle;
    private long _appliedPosition;

    internal BaseInMemoryProjectionStateWriter(InMemoryProjectionReplacement owner) => _owner = owner;

    internal void EnsureIndex(BaseInMemoryProjectionIndexHandle handle)
    {
        _owner.ThrowIfClosed();
        if (_handle is not null && !ReferenceEquals(_handle, handle)) throw new InvalidOperationException("base.vector.inMemory.projectionInvalid");
        _handle = handle;
    }

    internal void SetCarrier(BaseInMemoryProjectionIndexHandle handle, BaseInMemoryProjectionSourceRecord record)
    {
        EnsureIndex(handle);
        if (!_carriers.TryAdd(record.RecordId.Value, new InMemoryVectorCarrier(
            record.RecordId,
            record.Revision,
            record.LatestMutationPosition,
            BaseVector.Create(record.Vector.ToArray()))))
            throw new InvalidOperationException("base.vector.inMemory.projectionInvalid");
    }

    internal void AdvanceAppliedPosition(BaseInMemoryProjectionIndexHandle handle, long next)
    {
        EnsureIndex(handle);
        if (next < 0) throw new ArgumentOutOfRangeException(nameof(next));
        _appliedPosition = next;
    }

    internal (BaseInMemoryProjectionIndexHandle Handle, InMemoryVectorProjectionState State) Freeze()
    {
        BaseInMemoryProjectionIndexHandle handle = _handle ?? throw new InvalidOperationException("base.vector.inMemory.projectionInvalid");
        if (_appliedPosition != handle.Owner.GlobalMutationHighWater) throw new InvalidOperationException("base.vector.inMemory.projectionInvalid");
        var state = new InMemoryVectorProjectionState
        {
            AppliedThrough = _appliedPosition,
            Generation = checked(handle.Generation + 1),
            PurgeGeneration = handle.PurgeGeneration,
        };
        foreach ((string id, InMemoryVectorCarrier carrier) in _carriers) state.Carriers.Add(new string(id.AsSpan()), carrier.Copy());
        return (handle, state);
    }
}

internal sealed class InMemoryProjectionReplacement : IInMemoryProjectionReplacement
{
    private readonly InMemoryRecordStore _store;
    private int _closed;
    internal InMemoryProjectionReplacement(InMemoryRecordStore store, long expectedRootGeneration, long expectedProjectionGeneration)
    {
        _store = store;
        ExpectedRootGeneration = expectedRootGeneration;
        ExpectedProjectionGeneration = expectedProjectionGeneration;
        Writer = new BaseInMemoryProjectionStateWriter(this);
    }
    internal long ExpectedRootGeneration { get; }
    internal long ExpectedProjectionGeneration { get; }
    public BaseInMemoryProjectionStateWriter Writer { get; }
    internal void ThrowIfClosed() { if (Volatile.Read(ref _closed) != 0) throw new ObjectDisposedException(nameof(InMemoryProjectionReplacement)); }
    public async ValueTask<OperationResult<BaseInMemoryProjectionReplacementOutcome>> PublishAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _closed, 1, 0) != 0)
            return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.SessionClosed);
        (BaseInMemoryProjectionIndexHandle handle, InMemoryVectorProjectionState state) frozen;
        try { frozen = Writer.Freeze(); }
        catch (Exception) { return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.InvalidState); }
        return await _store.PublishProjectionReplacementAsync(ExpectedRootGeneration, ExpectedProjectionGeneration, frozen.handle, frozen.state, cancellationToken).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() { Interlocked.Exchange(ref _closed, 1); return ValueTask.CompletedTask; }
}

internal sealed class InMemoryProjectionReadSession : IInMemoryProjectionReadSession
{
    private static readonly ConcurrentDictionary<BaseVectorAuthoritySnapshot, ConcurrentDictionary<InMemoryProjectionReadSession, byte>> Active = new();
    internal static readonly IReadOnlyDictionary<string, InMemoryVectorCarrier> EmptyCarriers = new Dictionary<string, InMemoryVectorCarrier>(StringComparer.Ordinal);
    private readonly InMemoryVectorRootLease _lease;
    private readonly HashSet<BaseInMemoryProjectionSourceCursor> _cursors = new(ReferenceEqualityComparer.Instance);
    private int _disposed;

    internal InMemoryProjectionReadSession(InMemoryVectorRootLease lease, long rootGeneration, string storeIdentity, CollectionDefinition[] collections)
    {
        _lease = lease;
        ProjectionSnapshot = new BaseInMemoryProjectionSnapshot(lease.Root, rootGeneration, storeIdentity, collections);
        State = new BaseInMemoryProjectionStateReader(this);
        Snapshot = new BaseVectorAuthoritySnapshot
        {
            StoreIdentityDigest = ProjectionSnapshot.StoreIdentityDigest,
            RestoreEpoch = 0,
            SchemaGeneration = 1,
            CollectionId = "",
            PurgeGeneration = 0,
            VectorIndexId = "",
            VectorIndexGeneration = 0,
            VectorSpaceId = "",
            HighWatermark = new BaseMutationJournalPosition(ProjectionSnapshot.GlobalMutationHighWater),
        };
    }

    internal InMemoryStoreState Root => _lease.Root;
    internal IReadOnlyDictionary<string, StoredRecord> Records => Root.Collections.GetValueOrDefault(Snapshot.CollectionId)?.RecordsById
        ?? new Dictionary<string, StoredRecord>(StringComparer.Ordinal);
    public BaseVectorAuthoritySnapshot Snapshot { get; private set; }
    public BaseInMemoryProjectionSnapshot ProjectionSnapshot { get; }
    public BaseInMemoryProjectionStateReader State { get; }

    internal void Bind(BaseInMemoryProjectionIndexHandle handle)
    {
        ThrowIfClosed();
        ValidateHandle(handle);
        Snapshot = new BaseVectorAuthoritySnapshot
        {
            StoreIdentityDigest = ProjectionSnapshot.StoreIdentityDigest,
            RestoreEpoch = 0,
            SchemaGeneration = 1,
            CollectionId = handle.Collection.Id,
            PurgeGeneration = handle.PurgeGeneration,
            VectorIndexId = handle.Index.Id,
            VectorIndexGeneration = handle.Generation,
            VectorSpaceId = handle.Index.VectorSpaceId,
            HighWatermark = new BaseMutationJournalPosition(ProjectionSnapshot.GlobalMutationHighWater),
        };
        Active.GetOrAdd(Snapshot, static _ => new()).TryAdd(this, 0);
    }

    internal static InMemoryProjectionReadSession Find(BaseVectorAuthoritySnapshot snapshot) =>
        Active.TryGetValue(snapshot, out ConcurrentDictionary<InMemoryProjectionReadSession, byte>? sessions) && sessions.Keys.FirstOrDefault() is { } session
            ? session
            : throw new InvalidOperationException("The InMemory vector snapshot is no longer active.");

    internal void ValidateHandle(BaseInMemoryProjectionIndexHandle handle)
    {
        if (!ReferenceEquals(handle.Owner, ProjectionSnapshot) || !ProjectionSnapshot.GetIndexHandles().Contains(handle, ReferenceEqualityComparer.Instance))
            throw new InvalidOperationException("base.vector.inMemory.scanTargetInvalid");
    }

    internal void ThrowIfClosed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(InMemoryProjectionReadSession));
    }

    private bool IsClosed => Volatile.Read(ref _disposed) != 0;

    public ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseVectorCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default)
    {
        if (IsClosed)
            return ValueTask.FromResult(StoreFailure<RecordEnvelope[]>("base.vector.inMemory.sessionClosed", "The projection session is closed."));
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, StoredRecord> records = Root.Collections.GetValueOrDefault(collection.Id)?.RecordsById ?? new Dictionary<string, StoredRecord>();
        var result = new List<RecordEnvelope>(candidates.Length);
        foreach (BaseVectorCandidateIdentity candidate in candidates)
        {
            if (!records.TryGetValue(candidate.RecordId.Value, out StoredRecord? record) || record.Metadata.Revision != candidate.IndexedRevision)
                return ValueTask.FromResult(OperationResults.Conflict<RecordEnvelope[]>(new BaseError { Code = BaseVectorErrorCodes.SnapshotChanged, Message = "The vector snapshot changed.", Category = ErrorCategory.Conflict }));
            result.Add(new RecordEnvelope { CollectionId = collection.Id, Id = record.Id, Payload = RecordCloneHelpers.ClonePayload(record.Payload), Metadata = RecordCloneHelpers.CloneMetadata(record.Metadata) });
        }
        return ValueTask.FromResult(OperationResults.Ok(result.ToArray()));
    }

    public ValueTask<OperationResult<BaseInMemoryProjectionSourcePage>> EnumerateProjectionSourceAsync(BaseInMemoryProjectionIndexHandle index, BaseInMemoryProjectionSourceScanRequest request, CancellationToken cancellationToken)
    {
        if (IsClosed)
            return ValueTask.FromResult(StoreFailure<BaseInMemoryProjectionSourcePage>("base.vector.inMemory.sessionClosed", "The projection session is closed."));
        cancellationToken.ThrowIfCancellationRequested();
        try { ValidateHandle(index); }
        catch (InvalidOperationException) { return ValueTask.FromResult(Failure<BaseInMemoryProjectionSourcePage>("base.vector.inMemory.scanTargetInvalid", "The projection scan target is invalid.")); }
        BaseInMemoryProjectionSourceCursor? supplied = request.Cursor;
        IEnumerator<string> enumerator;
        if (supplied is null)
        {
            InMemoryCollectionState? state = Root.Collections.GetValueOrDefault(index.Collection.Id);
            enumerator = (state?.RecordIdsOrdinal ?? []).GetEnumerator();
        }
        else
        {
            if (!ReferenceEquals(supplied.Owner, this) || !ReferenceEquals(supplied.Handle, index) || supplied.Consumed || !_cursors.Remove(supplied))
                return ValueTask.FromResult(Failure<BaseInMemoryProjectionSourcePage>("base.vector.inMemory.scanContinuationInvalid", "The projection scan continuation is invalid."));
            supplied.Consumed = true;
            enumerator = supplied.Enumerator;
        }
        int limit = request.SourceEntryLimit ?? 256;
        int examined = 0;
        var records = new List<BaseInMemoryProjectionSourceRecord>();
        string? buffered = supplied?.NextRecordId;
        while (examined < limit)
        {
            string id;
            if (buffered is not null) { id = buffered; buffered = null; }
            else { if (!enumerator.MoveNext()) break; id = enumerator.Current; }
            cancellationToken.ThrowIfCancellationRequested();
            examined++;
            StoredRecord record = Root.Collections[index.Collection.Id].RecordsById[id];
            ProjectionVectorRead vectorRead = TryReadVector(record, index, cancellationToken, out BaseVector vector);
            if (vectorRead == ProjectionVectorRead.Absent) continue;
            if (vectorRead == ProjectionVectorRead.Invalid)
            {
                enumerator.Dispose();
                return ValueTask.FromResult(Failure<BaseInMemoryProjectionSourcePage>("base.vector.inMemory.projectionInvalid", "The vector projection source is invalid."));
            }
            records.Add(new BaseInMemoryProjectionSourceRecord(record.Id, record.Metadata.Revision!.Value, record.LatestMutationPosition, vector, ReadFilters(record, index)));
        }
        BaseInMemoryProjectionSourceCursor? next = null;
        if (examined == limit && enumerator.MoveNext())
        {
            next = new BaseInMemoryProjectionSourceCursor(this, index, enumerator, enumerator.Current);
            _cursors.Add(next);
        }
        else enumerator.Dispose();
        return ValueTask.FromResult(OperationResults.Ok(new BaseInMemoryProjectionSourcePage(records.ToArray(), examined, next)));
    }

    private enum ProjectionVectorRead { Absent, Valid, Invalid }

    private static ProjectionVectorRead TryReadVector(StoredRecord record, BaseInMemoryProjectionIndexHandle handle, CancellationToken cancellationToken, out BaseVector vector)
    {
        vector = default;
        string fieldName = handle.Collection.Fields?.SingleOrDefault(field => field.Id == handle.Index.VectorFieldId)?.Name ?? handle.Index.VectorFieldId;
        if (record.Payload.Fields is null || !record.Payload.Fields.TryGetValue(handle.Index.VectorFieldId, out JsonElement json) && !record.Payload.Fields.TryGetValue(fieldName, out json) || json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return ProjectionVectorRead.Absent;
        return InMemoryVectorMutationProjection.TryVector(json, handle.Index, cancellationToken, out vector)
            ? ProjectionVectorRead.Valid
            : ProjectionVectorRead.Invalid;
    }

    private static BaseInMemoryProjectionFilterSlot[] ReadFilters(StoredRecord record, BaseInMemoryProjectionIndexHandle handle) =>
        (handle.Index.FilterFieldIds ?? []).Select(fieldId =>
        {
            string fieldName = handle.Collection.Fields?.SingleOrDefault(field => field.Id == fieldId)?.Name ?? fieldId;
            if (record.Payload.Fields is null || !record.Payload.Fields.TryGetValue(fieldId, out JsonElement json) && !record.Payload.Fields.TryGetValue(fieldName, out json)) return BaseInMemoryProjectionFilterSlot.MissingValue(fieldId);
            return BaseInMemoryProjectionFilterSlot.Present(fieldId, InMemoryVectorProvider.ReadFilterValue(json, handle.Collection.Fields?.SingleOrDefault(field => field.Id == fieldId)?.Type == BaseFieldTypes.Id));
        }).ToArray();

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (Active.TryGetValue(Snapshot, out ConcurrentDictionary<InMemoryProjectionReadSession, byte>? sessions))
            {
                sessions.TryRemove(this, out _);
                if (sessions.IsEmpty) Active.TryRemove(new KeyValuePair<BaseVectorAuthoritySnapshot, ConcurrentDictionary<InMemoryProjectionReadSession, byte>>(Snapshot, sessions));
            }
            foreach (BaseInMemoryProjectionSourceCursor cursor in _cursors) cursor.Enumerator.Dispose();
            _cursors.Clear();
            _lease.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private static OperationResult<T> Failure<T>(string code, string message) => new()
    {
        Status = OperationStatus.ValidationFailed,
        Error = new BaseError { Code = code, Message = message, Category = ErrorCategory.Validation },
    };

    private static OperationResult<T> StoreFailure<T>(string code, string message) => new()
    {
        Status = OperationStatus.StoreError,
        Error = new BaseError { Code = code, Message = message, Category = ErrorCategory.Store },
    };
}

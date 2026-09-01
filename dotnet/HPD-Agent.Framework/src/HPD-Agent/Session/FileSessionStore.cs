using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.Serialization;
using HPD.Agent.Permissions;

namespace HPD.Agent;

public sealed record FileSessionStoreOptions(
    int SegmentEventCapacity = 1024,
    bool FlushToDiskOnCommit = true);

public sealed record FileSessionStoreDiagnostics(
    long SegmentReadCount,
    long SegmentBytesRead,
    long EventDecodeCount,
    long ObservationWaitCount);

/// <summary>
/// Segmented, append-oriented local-file implementation of the canonical thread journal.
/// This is a new storage format; it does not read the removed JsonSessionStore layout.
/// </summary>
public sealed class FileSessionStore : ISessionStore, IThreadDeltaStore, IPermissionPreferenceStore
{
    private const string DescriptorSchema = "hpd.agent.thread-descriptor";
    private const int DescriptorVersion = 2;

    private readonly string _basePath;
    private readonly FileSessionStoreOptions _options;
    private readonly ConcurrentDictionary<ThreadKey, ThreadRuntime> _runtimes = new();
    private readonly ConcurrentDictionary<ThreadKey, byte> _recoveredPendingDeltas = new();
    private readonly ConcurrentDictionary<ThreadKey, SemaphoreSlim> _pendingRecoveryGates = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionGates = new(StringComparer.Ordinal);
    private long _segmentReadCount;
    private long _segmentBytesRead;
    private long _eventDecodeCount;
    private long _observationWaitCount;

    /// <inheritdoc />
    public AgentEventCodec EventCodec { get; }

    /// <inheritdoc />
    public async ValueTask StageThreadDeltaAsync(
        ThreadKey thread,
        AgentEvent delta,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        await EnsurePendingDeltasRecoveredAsync(thread, cancellationToken).ConfigureAwait(false);
        _ = GetPendingIdentity(delta);
        var path = GetPendingDeltaPath(thread, delta);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EventCodec.RequireDurable(delta);
        var frame = EventCodec.Serialize(delta with { ThreadSequenceNumber = 0 }) + "\n";
        var bytes = Encoding.UTF8.GetBytes(frame);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024, true);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (_options.FlushToDiskOnCommit)
            stream.Flush(true);
    }

    /// <inheritdoc />
    public async ValueTask<ThreadEventAppendResult> FinalizeThreadDeltasAsync(
        ThreadKey thread,
        AgentEvent messageEnd,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        var path = GetPendingDeltaPath(thread, messageEnd);
        var deltas = await ReadPendingDeltasAsync(thread, path, cancellationToken).ConfigureAwait(false);
        var events = ThreadDeltaCoalescer.Coalesce(deltas, messageEnd);
        var result = await AppendThreadEventsAsync(thread, events, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (File.Exists(path))
            File.Delete(path);
        DeletePendingDirectoryIfEmpty(thread);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask RecoverThreadDeltasAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(GetThreadPath(thread), "pending-deltas");
        if (!Directory.Exists(directory))
            return;

        foreach (var path in Directory.EnumerateFiles(directory, "*.events").OrderBy(static path => path, StringComparer.Ordinal))
        {
            var deltas = await ReadPendingDeltasAsync(thread, path, cancellationToken).ConfigureAwait(false);
            if (deltas.Count == 0)
            {
                File.Delete(path);
                continue;
            }

            var first = deltas[0];
            if (await JournalContainsEventIdAsync(thread, first.EventId, cancellationToken).ConfigureAwait(false))
            {
                File.Delete(path);
                continue;
            }

            AgentEvent messageEnd = first switch
            {
                TextDeltaEvent text => new TextMessageEndEvent(text.MessageId),
                ReasoningDeltaEvent reasoning => new ReasoningMessageEndEvent(reasoning.MessageId),
                _ => throw new InvalidDataException($"Pending delta file '{path}' contains an unsupported event.")
            };
            messageEnd = messageEnd with
            {
                EventId = $"{first.EventId}-recovered-end",
                SessionId = first.SessionId,
                ThreadId = first.ThreadId,
                Metadata = first.Metadata,
                TraceId = first.TraceId,
                SpanId = first.SpanId,
                ParentSpanId = first.ParentSpanId,
                EventFlowId = first.EventFlowId,
                ThreadExecutionId = first.ThreadExecutionId
            };
            await AppendThreadEventsAsync(
                thread,
                ThreadDeltaCoalescer.Coalesce(deltas, messageEnd),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            File.Delete(path);
        }
        DeletePendingDirectoryIfEmpty(thread);
    }

    private async ValueTask<bool> JournalContainsEventIdAsync(
        ThreadKey thread,
        string eventId,
        CancellationToken cancellationToken)
    {
        var path = GetIndexPath(thread);
        if (!File.Exists(path))
            return false;
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 && StringComparer.Ordinal.Equals(parts[1], eventId))
                return true;
        }
        return false;
    }

    private async ValueTask EnsurePendingDeltasRecoveredAsync(
        ThreadKey thread,
        CancellationToken cancellationToken)
    {
        if (_recoveredPendingDeltas.ContainsKey(thread))
            return;
        var gate = _pendingRecoveryGates.GetOrAdd(thread, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recoveredPendingDeltas.ContainsKey(thread))
                return;
            await RecoverThreadDeltasAsync(thread, cancellationToken).ConfigureAwait(false);
            _recoveredPendingDeltas.TryAdd(thread, 0);
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<IReadOnlyList<AgentEvent>> ReadPendingDeltasAsync(
        ThreadKey thread,
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return [];
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > 0 && bytes[^1] != (byte)'\n')
        {
            var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
            await using var repair = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            repair.SetLength(lastNewline + 1L);
            repair.Flush(_options.FlushToDiskOnCommit);
            bytes = bytes.AsSpan(0, lastNewline + 1).ToArray();
        }

        var events = new List<AgentEvent>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            events.Add(DeserializeDurableEvent(line, thread, ReadJournalGeneration(thread), 0));
        }
        return events;
    }

    private string GetPendingDeltaPath(ThreadKey thread, AgentEvent evt)
    {
        var (messageId, kind) = GetPendingIdentity(evt);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(messageId))).ToLowerInvariant();
        return Path.Combine(GetThreadPath(thread), "pending-deltas", $"{key}.{kind}.events");
    }

    private static (string MessageId, string Kind) GetPendingIdentity(AgentEvent evt) => evt switch
    {
        TextDeltaEvent delta => (delta.MessageId, "text"),
        TextMessageEndEvent end => (end.MessageId, "text"),
        ReasoningDeltaEvent delta => (delta.MessageId, "reasoning"),
        ReasoningMessageEndEvent end => (end.MessageId, "reasoning"),
        _ => throw new ArgumentException("Event is not a supported delta or message boundary.", nameof(evt))
    };

    private void DeletePendingDirectoryIfEmpty(ThreadKey thread)
    {
        var directory = Path.Combine(GetThreadPath(thread), "pending-deltas");
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }

    public FileSessionStore(
        string basePath,
        AgentEventCodec eventCodec,
        FileSessionStoreOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        EventCodec = eventCodec ?? throw new ArgumentNullException(nameof(eventCodec));
        _options = options ?? new FileSessionStoreOptions();
        if (_options.SegmentEventCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "SegmentEventCapacity must be positive.");
        _basePath = Path.GetFullPath(basePath);
        Directory.CreateDirectory(GetSessionsPath());
    }

    public FileSessionStoreDiagnostics GetDiagnostics() => new(
        Interlocked.Read(ref _segmentReadCount),
        Interlocked.Read(ref _segmentBytesRead),
        Interlocked.Read(ref _eventDecodeCount),
        Interlocked.Read(ref _observationWaitCount));

    public async Task<Session?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var path = GetSessionMetadataPath(sessionId);
        if (!File.Exists(path))
            return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, true);
        var session = await JsonSerializer.DeserializeAsync(stream, SessionJsonContext.Combined.Session, cancellationToken)
            .ConfigureAwait(false);
        return session is not null && await ThreadForkVisibility.IsSessionVisibleAsync(this, session, cancellationToken)
            .ConfigureAwait(false) ? session : null;
    }

    public async Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var gate = _sessionGates.GetOrAdd(session.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(session, SessionJsonContext.Combined.Session);
            await WriteAtomicallyAsync(GetSessionMetadataPath(session.Id), json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<SessionPreparationResult> TryPrepareSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var gate = _sessionGates.GetOrAdd(session.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetSessionMetadataPath(session.Id);
            if (File.Exists(path))
            {
                await using var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, true);
                var current = await JsonSerializer.DeserializeAsync(
                    read, SessionJsonContext.Combined.Session, cancellationToken).ConfigureAwait(false);
                return current?.Preparation == session.Preparation
                    ? SessionPreparationResult.ExistingOwned
                    : SessionPreparationResult.Conflict;
            }
            var json = JsonSerializer.Serialize(session, SessionJsonContext.Combined.Session);
            await WriteAtomicallyAsync(path, json, cancellationToken).ConfigureAwait(false);
            return SessionPreparationResult.Created;
        }
        finally { gate.Release(); }
    }

    public async Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSessionsPath();
        var candidates = !Directory.Exists(path)
            ? []
            : Directory.EnumerateDirectories(path).Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToList();
        var visible = new List<string>();
        foreach (var sessionId in candidates)
            if (await LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false) is not null)
                visible.Add(sessionId);
        return visible;
    }

    /// <inheritdoc />
    public async ValueTask<PermissionPreferenceSnapshot> ReadAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var gate = _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadPreferenceStateAsync(sessionId, cancellationToken).ConfigureAwait(false)).Snapshot; }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<PermissionPreferenceCommitResult> CommitAsync(
        PermissionPreferenceCommit commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.AuditThread.SessionId != commit.SessionId)
            throw new InvalidOperationException("Permission audit thread must belong to the preference session.");
        var gate = _sessionGates.GetOrAdd(commit.SessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadPreferenceStateAsync(commit.SessionId, cancellationToken).ConfigureAwait(false);
            var replay = state.Outbox.FirstOrDefault(value => value.IdempotencyKey == commit.IdempotencyKey);
            if (replay is not null)
            {
                if (replay.State == PermissionPreferenceOutboxState.Pending ||
                    replay is { State: PermissionPreferenceOutboxState.Claimed, ClaimExpiresAt: { } expiry } &&
                    expiry <= DateTimeOffset.UtcNow)
                {
                    replay = replay with
                    {
                        State = PermissionPreferenceOutboxState.Claimed,
                        ClaimToken = Guid.NewGuid().ToString("N"),
                        ClaimantId = commit.PublisherClaimantId,
                        ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
                    };
                    state = state with
                    {
                        Outbox = state.Outbox.Select(value => value.IdempotencyKey == commit.IdempotencyKey
                            ? replay : value).ToArray()
                    };
                    await WritePreferenceStateAsync(commit.SessionId, state, cancellationToken).ConfigureAwait(false);
                }
                return new PermissionPreferenceCommitResult
                {
                    Status = PermissionPreferenceCommitStatus.AlreadyCommitted,
                    CurrentVersion = state.Snapshot.Version,
                    Outbox = replay
                };
            }
            if (state.Snapshot.Version != commit.ExpectedVersion)
                return new PermissionPreferenceCommitResult
                {
                    Status = PermissionPreferenceCommitStatus.VersionConflict,
                    CurrentVersion = state.Snapshot.Version
                };
            if (commit.Replacement.Version != commit.ExpectedVersion + 1)
                throw new InvalidOperationException("Permission replacement version must advance exactly once.");
            var wal = new FilePermissionPreferenceWal(commit, state);
            await WriteAtomicallyAsync(
                GetPermissionPreferenceWalPath(commit.SessionId),
                JsonSerializer.Serialize(wal),
                cancellationToken).ConfigureAwait(false);
            state = await RecoverPreferenceWalAsync(commit.SessionId, cancellationToken).ConfigureAwait(false);
            var outbox = state.Outbox.Single(value => value.IdempotencyKey == commit.IdempotencyKey);
            return new PermissionPreferenceCommitResult
            {
                Status = PermissionPreferenceCommitStatus.Committed,
                CurrentVersion = commit.Replacement.Version,
                Outbox = outbox
            };
        }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PermissionPreferenceOutboxRecord>> ClaimPendingPublicationAsync(
        string sessionId,
        string claimantId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimantId);
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        var gate = _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadPreferenceStateAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var ids = state.Outbox.Where(value => value.State == PermissionPreferenceOutboxState.Pending ||
                    value is { State: PermissionPreferenceOutboxState.Claimed, ClaimExpiresAt: { } expiry } && expiry <= now)
                .OrderBy(static value => value.ThreadSequenceNumber)
                .Take(maxCount)
                .Select(static value => value.SettlementId)
                .ToHashSet(StringComparer.Ordinal);
            var claimed = new List<PermissionPreferenceOutboxRecord>();
            var replacements = state.Outbox.Select(value =>
            {
                if (!ids.Contains(value.SettlementId)) return value;
                var updated = value with
                {
                    State = PermissionPreferenceOutboxState.Claimed,
                    ClaimToken = Guid.NewGuid().ToString("N"),
                    ClaimantId = claimantId,
                    ClaimExpiresAt = now.AddMinutes(1)
                };
                claimed.Add(updated);
                return updated;
            }).ToArray();
            if (claimed.Count > 0)
                await WritePreferenceStateAsync(
                    sessionId, state with { Outbox = replacements }, cancellationToken).ConfigureAwait(false);
            return claimed;
        }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<bool> AcknowledgePublicationAsync(
        string settlementId,
        string claimToken,
        CancellationToken cancellationToken)
    {
        foreach (var sessionId in await ListSessionIdsAsync(cancellationToken).ConfigureAwait(false))
        {
            var gate = _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var state = await ReadPreferenceStateAsync(sessionId, cancellationToken).ConfigureAwait(false);
                var current = state.Outbox.FirstOrDefault(value => value.SettlementId == settlementId);
                if (current is null) continue;
                if (current.State == PermissionPreferenceOutboxState.Acknowledged) return true;
                if (current.State != PermissionPreferenceOutboxState.Claimed || current.ClaimToken != claimToken)
                    return false;
                var replacements = state.Outbox.Select(value => value.SettlementId == settlementId
                    ? value with
                    {
                        State = PermissionPreferenceOutboxState.Acknowledged,
                        ClaimToken = null,
                        ClaimantId = null,
                        ClaimExpiresAt = null
                    }
                    : value).ToArray();
                await WritePreferenceStateAsync(
                    sessionId, state with { Outbox = replacements }, cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally { gate.Release(); }
        }
        return false;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var gate = _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (key, runtime) in _runtimes.Where(pair => pair.Key.SessionId == sessionId))
            {
                if (_runtimes.TryRemove(key, out _))
                    await runtime.MarkDeletedAsync(cancellationToken).ConfigureAwait(false);
            }

            var path = GetSessionPath(sessionId);
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask<ThreadEventAppendResult> AppendThreadEventsAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> events,
        ThreadAppendCondition condition = default,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            throw new ArgumentException("At least one event is required.", nameof(events));
        var proposed = events.Select(evt => ThreadEventValidation.PrepareForAppend(thread.SessionId, thread.ThreadId, evt)).ToArray();
        return GetRuntime(thread).AppendAsync(proposed, condition, cancellationToken);
    }

    public ValueTask<ThreadJournalReplaceResult> ReplaceThreadEventsAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> events,
        ThreadJournalCursor expectedCursor,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            throw new ArgumentException("A replacement journal cannot be empty.", nameof(events));
        if (!ThreadExists(thread))
            throw new InvalidOperationException($"Thread '{thread.ThreadId}' does not exist.");
        var proposed = events.Select(evt => ThreadEventValidation.PrepareForAppend(
            thread.SessionId, thread.ThreadId, evt)).ToArray();
        return GetRuntime(thread).ReplaceAsync(proposed, expectedCursor, cancellationToken);
    }

    public async ValueTask<ThreadDescriptor?> GetThreadAsync(ThreadKey thread, CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        if (!ThreadExists(thread))
            return null;
        await EnsurePendingDeltasRecoveredAsync(thread, cancellationToken).ConfigureAwait(false);
        var descriptor = await GetRuntime(thread).GetDescriptorAsync(cancellationToken).ConfigureAwait(false);
        return descriptor is not null && descriptor.Kind != ThreadKind.FrameworkInternal &&
            await ThreadForkVisibility.IsVisibleAsync(this, descriptor, cancellationToken).ConfigureAwait(false)
                ? descriptor
                : null;
    }

    public async IAsyncEnumerable<ThreadDescriptor> ListThreadsAsync(
        string sessionId,
        ThreadListRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        var root = GetThreadsPath(sessionId);
        if (!Directory.Exists(root))
            yield break;

        var descriptorPaths = Directory
            .EnumerateFiles(root, "thread.descriptor.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
        var count = 0;
        foreach (var descriptorPath in descriptorPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(descriptorPath)!;
            var threadId = Path.GetRelativePath(root, directory)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var key = new ThreadKey(sessionId, threadId);
            var descriptor = await GetRuntime(key).GetDescriptorAsync(cancellationToken).ConfigureAwait(false);
            if (descriptor is not null && descriptor.Key != key)
                continue;
            if (descriptor is null || descriptor.Kind == ThreadKind.FrameworkInternal ||
                !await ThreadForkVisibility.IsVisibleAsync(this, descriptor, cancellationToken).ConfigureAwait(false) ||
                (!request.IncludeHidden && descriptor.Visibility == ThreadVisibility.Hidden))
                continue;
            yield return descriptor;
            if (++count >= request.MaxCount)
                yield break;
        }
    }

    public async ValueTask<ThreadEventHead?> GetThreadEventHeadAsync(ThreadKey thread, CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        if (!ThreadExists(thread))
            return null;
        await EnsurePendingDeltasRecoveredAsync(thread, cancellationToken).ConfigureAwait(false);
        return await GetRuntime(thread).GetHeadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ThreadEventBatch> ReadThreadEventsAsync(
        ThreadKey thread,
        ThreadEventReadRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ValidateReadRequest(request);
        if (!ThreadExists(thread))
            yield break;
        await EnsurePendingDeltasRecoveredAsync(thread, cancellationToken).ConfigureAwait(false);

        var runtime = GetRuntime(thread);
        var boundary = await runtime.CaptureReadBoundaryAsync(request.After, request.Through, cancellationToken).ConfigureAwait(false);
        if (boundary is null)
            yield break;

        var pending = new List<AgentEvent>(request.MaxBatchEventCount);
        foreach (var segment in boundary.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.LastSequence <= request.After.SequenceNumber || segment.FirstSequence > boundary.Through)
                continue;

            await foreach (var evt in ReadSegmentEventsAsync(thread, boundary.Generation, segment, cancellationToken).ConfigureAwait(false))
            {
                if (evt.ThreadSequenceNumber <= request.After.SequenceNumber || evt.ThreadSequenceNumber > boundary.Through)
                    continue;
                pending.Add(evt);
                if (pending.Count == request.MaxBatchEventCount)
                {
                    yield return ToBatch(boundary.Generation, pending);
                    pending = new List<AgentEvent>(request.MaxBatchEventCount);
                }
            }
        }

        if (pending.Count > 0)
            yield return ToBatch(boundary.Generation, pending);
    }

    public async IAsyncEnumerable<ThreadEventBatch> ObserveThreadEventsAsync(
        ThreadKey thread,
        ThreadJournalCursor after,
        ThreadObservationOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ArgumentNullException.ThrowIfNull(options);
        if (after.Generation <= 0 || after.SequenceNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(after));
        if (options.MaxBatchEventCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (!ThreadExists(thread))
            yield break;
        await EnsurePendingDeltasRecoveredAsync(thread, cancellationToken).ConfigureAwait(false);

        var runtime = GetRuntime(thread);
        var cursor = after;
        while (true)
        {
            var observation = await runtime.CaptureObservationAsync(cursor, cancellationToken).ConfigureAwait(false);
            if (observation.Head.SequenceNumber > cursor.SequenceNumber)
            {
                await foreach (var batch in ReadThreadEventsAsync(
                    thread,
                    new ThreadEventReadRequest(cursor, observation.Head.SequenceNumber, options.MaxBatchEventCount),
                    cancellationToken).ConfigureAwait(false))
                {
                    yield return batch;
                    cursor = new ThreadJournalCursor(batch.Generation, batch.LastThreadSequenceNumber);
                }
                continue;
            }

            Interlocked.Increment(ref _observationWaitCount);
            await observation.CommitSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteThreadAsync(string sessionId, string threadId, CancellationToken cancellationToken = default)
    {
        var key = new ThreadKey(sessionId, threadId);
        if (_runtimes.TryRemove(key, out var runtime))
            await runtime.MarkDeletedAsync(cancellationToken).ConfigureAwait(false);
        var path = GetThreadPath(key);
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    public async Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - inactivityThreshold;
        var root = GetSessionsPath();
        if (!Directory.Exists(root))
            return 0;
        var ids = Directory.EnumerateDirectories(root)
            .Where(path => Directory.GetLastWriteTimeUtc(path) < cutoff)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        if (!dryRun)
        {
            foreach (var id in ids)
                await DeleteSessionAsync(id, cancellationToken).ConfigureAwait(false);
        }
        return ids.Length;
    }

    private ThreadRuntime GetRuntime(ThreadKey key)
        => _runtimes.GetOrAdd(key, thread => new ThreadRuntime(this, thread));

    private bool ThreadExists(ThreadKey key)
        => Directory.Exists(GetThreadPath(key));

    private string GetSessionsPath() => Path.Combine(_basePath, "sessions");
    private string GetSessionPath(string sessionId) => Path.Combine(GetSessionsPath(), sessionId);
    private string GetSessionMetadataPath(string sessionId) => Path.Combine(GetSessionPath(sessionId), "session.meta.json");
    private string GetPermissionPreferencePath(string sessionId) =>
        Path.Combine(GetSessionPath(sessionId), "permission-preferences.v9.json");
    private string GetPermissionPreferenceWalPath(string sessionId) =>
        Path.Combine(GetSessionPath(sessionId), "permission-preferences.v9.wal.json");
    private string GetThreadsPath(string sessionId) => Path.Combine(GetSessionPath(sessionId), "threads");

    private async ValueTask<FilePermissionPreferenceState> ReadPreferenceStateAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (File.Exists(GetPermissionPreferenceWalPath(sessionId)))
            return await RecoverPreferenceWalAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return await ReadPreferenceStateFileAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<FilePermissionPreferenceState> ReadPreferenceStateFileAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var path = GetPermissionPreferencePath(sessionId);
        if (!File.Exists(path))
            return new FilePermissionPreferenceState(new PermissionPreferenceSnapshot(0, []), []);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, true);
        return await JsonSerializer.DeserializeAsync<FilePermissionPreferenceState>(
                stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Permission preference state '{path}' is empty.");
    }

    private async ValueTask<FilePermissionPreferenceState> RecoverPreferenceWalAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var walPath = GetPermissionPreferenceWalPath(sessionId);
        await using var walStream = new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, true);
        var wal = await JsonSerializer.DeserializeAsync<FilePermissionPreferenceWal>(
                walStream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Permission preference WAL '{walPath}' is empty.");
        var state = await ReadPreferenceStateFileAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (state.Outbox.Any(value => value.IdempotencyKey == wal.Commit.IdempotencyKey))
        {
            File.Delete(walPath);
            return state;
        }

        PermissionPreferenceChangedEvent? committedEvent = null;
        var head = await GetThreadEventHeadAsync(wal.Commit.AuditThread, cancellationToken).ConfigureAwait(false);
        if (head is not null)
        {
            await foreach (var batch in ReadThreadEventsAsync(
                wal.Commit.AuditThread,
                new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Cursor.Generation), head.Cursor.SequenceNumber),
                cancellationToken).ConfigureAwait(false))
            {
                committedEvent = batch.Events.OfType<PermissionPreferenceChangedEvent>().FirstOrDefault(value =>
                    value.PreferenceId == wal.Commit.Event.PreferenceId && value.Key == wal.Commit.Event.Key &&
                    value.Decision == wal.Commit.Event.Decision && value.Persistence == wal.Commit.Event.Persistence);
                if (committedEvent is not null) break;
            }
        }
        if (committedEvent is null)
        {
            var appended = await AppendThreadEventsAsync(
                wal.Commit.AuditThread, [wal.Commit.Event], cancellationToken: cancellationToken).ConfigureAwait(false);
            committedEvent = (PermissionPreferenceChangedEvent)appended.CommittedEvents.Single();
        }
        var settlementId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(wal.Commit.IdempotencyKey))).ToLowerInvariant();
        var outbox = new PermissionPreferenceOutboxRecord
        {
            SettlementId = settlementId,
            ClaimToken = Guid.NewGuid().ToString("N"),
            ClaimantId = wal.Commit.PublisherClaimantId,
            ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            State = PermissionPreferenceOutboxState.Claimed,
            SessionId = wal.Commit.SessionId,
            AuditThread = wal.Commit.AuditThread,
            CommittedEvent = committedEvent,
            ThreadSequenceNumber = committedEvent.ThreadSequenceNumber,
            IdempotencyKey = wal.Commit.IdempotencyKey
        };
        state = new FilePermissionPreferenceState(
            wal.Commit.Replacement,
            wal.PreviousState.Outbox.Append(outbox).ToArray());
        await WritePreferenceStateAsync(sessionId, state, cancellationToken).ConfigureAwait(false);
        File.Delete(walPath);
        return state;
    }

    private Task WritePreferenceStateAsync(
        string sessionId,
        FilePermissionPreferenceState state,
        CancellationToken cancellationToken) =>
        WriteAtomicallyAsync(
            GetPermissionPreferencePath(sessionId),
            JsonSerializer.Serialize(state),
            cancellationToken);

    private sealed record FilePermissionPreferenceState(
        PermissionPreferenceSnapshot Snapshot,
        IReadOnlyList<PermissionPreferenceOutboxRecord> Outbox);
    private sealed record FilePermissionPreferenceWal(
        PermissionPreferenceCommit Commit,
        FilePermissionPreferenceState PreviousState);
    private string GetThreadPath(ThreadKey key) => Path.Combine(GetThreadsPath(key.SessionId), key.ThreadId);
    private string GetJournalPath(ThreadKey key) => Path.Combine(GetThreadPath(key), "journal");
    private string GetJournalGenerationPath(ThreadKey key) => Path.Combine(GetJournalPath(key), "generation");
    private string GetDescriptorPath(ThreadKey key) => Path.Combine(GetThreadPath(key), "thread.descriptor.json");
    private string GetIndexPath(ThreadKey key) => Path.Combine(GetThreadPath(key), "journal.index");
    private string GetSegmentPath(ThreadKey key, long start) => Path.Combine(GetJournalPath(key), $"segment-{start:D20}.events");

    private static ThreadEventBatch ToBatch(long generation, List<AgentEvent> events)
        => new(events.ToArray(), generation, events[0].ThreadSequenceNumber, events[^1].ThreadSequenceNumber);

    private async IAsyncEnumerable<AgentEvent> ReadSegmentEventsAsync(
        ThreadKey thread,
        long generation,
        SegmentSnapshot segment,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!File.Exists(segment.Path))
            throw new InvalidDataException($"Journal segment '{segment.Path}' is missing.");
        var bytes = await File.ReadAllBytesAsync(segment.Path, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _segmentReadCount);
        Interlocked.Add(ref _segmentBytesRead, bytes.LongLength);
        var length = Math.Min(bytes.LongLength, segment.Length);
        var start = 0;
        for (var index = 0; index < length; index++)
        {
            if (bytes[index] != (byte)'\n')
                continue;
            if (index > start)
            {
                var frame = DeserializeFrame(bytes.AsSpan(start, index - start), thread, generation, segment.FirstSequence);
                Interlocked.Add(ref _eventDecodeCount, frame.Count);
                foreach (var evt in frame)
                    yield return evt;
            }
            start = index + 1;
        }
        if (start < length)
            throw new InvalidDataException($"Journal segment '{segment.Path}' ends with an incomplete committed frame.");
    }

    private async Task WriteAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, true);
    }

    private IReadOnlyList<AgentEvent> DeserializeFrame(
        ReadOnlySpan<byte> bytes,
        ThreadKey thread,
        long generation,
        long firstSequence)
    {
        using var document = JsonDocument.Parse(bytes.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Journal frame must be a JSON array.");
        var events = new List<AgentEvent>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var sequence = element.TryGetProperty("threadSequenceNumber", out var sequenceProperty) &&
                sequenceProperty.TryGetInt64(out var persistedSequence)
                ? persistedSequence
                : firstSequence + events.Count;
            events.Add(DeserializeDurableEvent(element.GetRawText(), thread, generation, sequence));
        }
        return events;
    }

    private AgentEvent DeserializeDurableEvent(string json, ThreadKey thread, long generation, long sequence)
    {
        try
        {
            return EventCodec.DeserializeEvent(json);
        }
        catch (UnknownAgentEventDiscriminatorException exception)
        {
            throw new UnknownDurableAgentEventException(
                exception.Discriminator,
                thread.SessionId,
                thread.ThreadId,
                generation,
                sequence,
                exception.CodecDigest,
                exception);
        }
    }

    private long ReadJournalGeneration(ThreadKey thread)
    {
        var path = GetJournalGenerationPath(thread);
        return File.Exists(path) && long.TryParse(File.ReadAllText(path), out var generation) ? generation : 0;
    }

    private string SerializeFrame(IEnumerable<AgentEvent> events) =>
        "[" + string.Join(',', events.Select(evt => EventCodec.Serialize(evt))) + "]\n";

    private static void ValidateThreadKey(ThreadKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.ThreadId);
    }

    private static void ValidateReadRequest(ThreadEventReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.After.Generation <= 0 || request.After.SequenceNumber < 0 || request.MaxBatchEventCount <= 0 || request.Through is long through && through < request.After.SequenceNumber)
            throw new ArgumentOutOfRangeException(nameof(request));
    }

    private sealed class ThreadRuntime
    {
        private readonly FileSessionStore _store;
        private readonly ThreadKey _key;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly HashSet<string> _eventIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
        private TaskCompletionSource _commitSignal = NewSignal();
        private FileThreadDescriptorState? _state;
        private bool _initialized;
        private bool _deleted;

        public ThreadRuntime(FileSessionStore store, ThreadKey key)
        {
            _store = store;
            _key = key;
        }

        public async ValueTask<ThreadEventAppendResult> AppendAsync(
            IReadOnlyList<AgentEvent> proposed,
            ThreadAppendCondition condition,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource signal;
            ThreadEventAppendResult result;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfDeleted();
                var head = _state?.Descriptor.Head ?? 0;
                var generation = _state?.Descriptor.Generation ?? 1;
                var current = new ThreadJournalCursor(generation, head);
                if (condition.ExpectedCursor is ThreadJournalCursor expected && expected != current)
                    throw new ThreadAppendConflictException(_key, expected, current);
                var duplicate = proposed.FirstOrDefault(evt => _eventIds.Contains(evt.EventId));
                if (duplicate is not null || proposed.Select(evt => evt.EventId).Distinct(StringComparer.Ordinal).Count() != proposed.Count)
                    throw new InvalidOperationException("Thread journal append contains an already committed or duplicate EventId.");

                var committed = proposed.Select((evt, index) => evt with { ThreadSequenceNumber = head + index + 1 }).ToArray();
                var segmentStart = _state?.CurrentSegmentStart ?? 1;
                var segmentCount = _state?.CurrentSegmentEventCount ?? 0;
                if (segmentCount > 0 && segmentCount + committed.Length > _store._options.SegmentEventCapacity)
                {
                    segmentStart = committed[0].ThreadSequenceNumber;
                    segmentCount = 0;
                }

                var segmentPath = _store.GetSegmentPath(_key, segmentStart);
                Directory.CreateDirectory(Path.GetDirectoryName(segmentPath)!);
                await EnsureJournalGenerationAsync(generation, cancellationToken).ConfigureAwait(false);
                foreach (var evt in committed)
                    _store.EventCodec.RequireDurable(evt);
                var frame = _store.SerializeFrame(committed);
                await AppendTextAsync(segmentPath, frame, cancellationToken).ConfigureAwait(false);

                var indexText = string.Concat(committed.Select(evt => $"{evt.ThreadSequenceNumber}\t{evt.EventId}\t{segmentStart}\n"));
                await AppendTextAsync(_store.GetIndexPath(_key), indexText, cancellationToken).ConfigureAwait(false);

                var descriptor = _state?.Descriptor;
                foreach (var evt in committed)
                {
                    _eventIds.Add(evt.EventId);
                    descriptor = ThreadDescriptorProjection.Apply(_key, descriptor, _messageIds, evt, generation, evt.ThreadSequenceNumber);
                }
                _state = new FileThreadDescriptorState(
                    DescriptorSchema,
                    DescriptorVersion,
                    descriptor!,
                    _messageIds.ToArray(),
                    segmentStart,
                    segmentCount + committed.Length);
                await SaveStateAsync(cancellationToken).ConfigureAwait(false);

                result = new ThreadEventAppendResult(
                    committed,
                    new ThreadJournalCursor(generation, head),
                    new ThreadJournalCursor(generation, descriptor!.Head));
                signal = _commitSignal;
                _commitSignal = NewSignal();
            }
            finally
            {
                _gate.Release();
            }
            signal.TrySetResult();
            return result;
        }

        public async ValueTask<ThreadJournalReplaceResult> ReplaceAsync(
            IReadOnlyList<AgentEvent> proposed,
            ThreadJournalCursor expectedCursor,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource signal;
            ThreadJournalReplaceResult result;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfDeleted();
                var previousCursor = new ThreadJournalCursor(
                    _state?.Descriptor.Generation ?? 1,
                    _state?.Descriptor.Head ?? 0);
                if (previousCursor != expectedCursor)
                    throw new ThreadAppendConflictException(_key, expectedCursor, previousCursor);
                var replacementGeneration = checked(previousCursor.Generation + 1);
                if (proposed.Select(evt => evt.EventId).Distinct(StringComparer.Ordinal).Count() != proposed.Count)
                    throw new InvalidOperationException("A replacement journal cannot contain duplicate EventIds.");

                var committed = proposed.Select((evt, index) => evt with { ThreadSequenceNumber = index + 1 }).ToArray();
                var replacementRoot = _store.GetThreadPath(_key) + $".replacement-{Guid.NewGuid():N}";
                var replacementJournal = Path.Combine(replacementRoot, "journal");
                Directory.CreateDirectory(replacementJournal);
                var segmentPath = Path.Combine(replacementJournal, "segment-00000000000000000001.events");
                foreach (var evt in committed)
                    _store.EventCodec.RequireDurable(evt);
                var frame = _store.SerializeFrame(committed);
                await File.WriteAllTextAsync(segmentPath, frame, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(replacementJournal, "generation"),
                    replacementGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new UTF8Encoding(false),
                    cancellationToken).ConfigureAwait(false);

                _eventIds.Clear();
                _messageIds.Clear();
                ThreadDescriptor? descriptor = null;
                var index = new StringBuilder();
                foreach (var evt in committed)
                {
                    _eventIds.Add(evt.EventId);
                    descriptor = ThreadDescriptorProjection.Apply(
                        _key, descriptor, _messageIds, evt, replacementGeneration, evt.ThreadSequenceNumber);
                    index.Append(evt.ThreadSequenceNumber).Append('\t').Append(evt.EventId).Append("\t1\n");
                }

                var replacementState = new FileThreadDescriptorState(
                    DescriptorSchema,
                    DescriptorVersion,
                    descriptor!,
                    _messageIds.ToArray(),
                    1,
                    committed.Length);
                await File.WriteAllTextAsync(
                    Path.Combine(replacementRoot, "journal.index"),
                    index.ToString(),
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(replacementRoot, "thread.descriptor.json"),
                    JsonSerializer.Serialize(replacementState, SessionJsonContext.Combined.FileThreadDescriptorState),
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);

                var threadRoot = _store.GetThreadPath(_key);
                var oldJournal = _store.GetJournalPath(_key);
                var retiredJournal = Path.Combine(threadRoot, $"retired-{Guid.NewGuid():N}");
                if (Directory.Exists(oldJournal))
                    Directory.Move(oldJournal, retiredJournal);
                Directory.Move(replacementJournal, oldJournal);
                File.Move(Path.Combine(replacementRoot, "journal.index"), _store.GetIndexPath(_key), true);
                File.Move(Path.Combine(replacementRoot, "thread.descriptor.json"), _store.GetDescriptorPath(_key), true);
                Directory.Delete(replacementRoot, true);
                if (Directory.Exists(retiredJournal))
                    Directory.Delete(retiredJournal, true);

                _state = replacementState;
                result = new ThreadJournalReplaceResult(
                    committed,
                    previousCursor,
                    new ThreadJournalCursor(replacementGeneration, committed.Length));
                signal = _commitSignal;
                _commitSignal = NewSignal();
            }
            finally
            {
                _gate.Release();
            }

            signal.TrySetException(new ThreadJournalReplacedException(
                _key, result.PreviousCursor, result.CurrentCursor));
            return result;
        }

        public async ValueTask<ThreadDescriptor?> GetDescriptorAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfDeleted();
                return _state?.Descriptor;
            }
            finally { _gate.Release(); }
        }

        public async ValueTask<ThreadEventHead?> GetHeadAsync(CancellationToken cancellationToken)
        {
            var descriptor = await GetDescriptorAsync(cancellationToken).ConfigureAwait(false);
            return descriptor is null ? null : new ThreadEventHead(descriptor.Generation, descriptor.Head, descriptor.UpdatedAt);
        }

        public async ValueTask<ReadBoundary?> CaptureReadBoundaryAsync(ThreadJournalCursor after, long? through, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfDeleted();
                var head = _state?.Descriptor.Head ?? 0;
                var generation = _state?.Descriptor.Generation ?? 1;
                var current = new ThreadJournalCursor(generation, head);
                if (after.Generation != generation || after.SequenceNumber > head)
                    throw new ThreadCursorConflictException(_key, after, current);
                if (head == 0 || after.SequenceNumber == head)
                    return null;
                var limit = Math.Min(through ?? head, head);
                var paths = Directory.EnumerateFiles(_store.GetJournalPath(_key), "segment-*.events")
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => new { Path = path, Start = ParseSegmentStart(path), Length = new FileInfo(path).Length })
                    .ToArray();
                var segments = new List<SegmentSnapshot>(paths.Length);
                for (var index = 0; index < paths.Length; index++)
                {
                    var last = index + 1 < paths.Length ? paths[index + 1].Start - 1 : head;
                    segments.Add(new SegmentSnapshot(paths[index].Path, paths[index].Start, last, paths[index].Length));
                }
                return new ReadBoundary(generation, limit, segments);
            }
            finally { _gate.Release(); }
        }

        public async ValueTask<ObservationSnapshot> CaptureObservationAsync(ThreadJournalCursor after, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfDeleted();
                var head = _state?.Descriptor.Head ?? 0;
                var current = new ThreadJournalCursor(_state?.Descriptor.Generation ?? 1, head);
                if (after.Generation != current.Generation || after.SequenceNumber > head)
                    throw new ThreadCursorConflictException(_key, after, current);
                return new ObservationSnapshot(current, _commitSignal.Task);
            }
            finally { _gate.Release(); }
        }

        public async ValueTask MarkDeletedAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource signal;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _deleted = true;
                signal = _commitSignal;
                _commitSignal = NewSignal();
            }
            finally { _gate.Release(); }
            signal.TrySetException(new ThreadDeletedException(_key));
        }

        private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
                return;
            _initialized = true;
            var descriptorPath = _store.GetDescriptorPath(_key);
            if (!File.Exists(descriptorPath))
            {
                if (Directory.Exists(_store.GetJournalPath(_key)) && Directory.EnumerateFiles(_store.GetJournalPath(_key), "segment-*.events").Any())
                    await RecoverAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            await using (var stream = new FileStream(descriptorPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, true))
            {
                _state = await JsonSerializer.DeserializeAsync(stream, SessionJsonContext.Combined.FileThreadDescriptorState, cancellationToken)
                    .ConfigureAwait(false) ?? throw new InvalidDataException($"Thread descriptor '{descriptorPath}' is empty.");
            }
            if (_state.Descriptor.Key != _key)
                throw new InvalidDataException($"Thread descriptor '{descriptorPath}' has conflicting scope.");
            if (_state.Schema != DescriptorSchema || _state.Version != DescriptorVersion)
            {
                await RecoverAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            await EnsureJournalGenerationAsync(_state.Descriptor.Generation, cancellationToken).ConfigureAwait(false);
            foreach (var id in _state.MessageIds)
                _messageIds.Add(id);
            await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            if (_eventIds.Count != _state.Descriptor.Head ||
                await RepairPartialTailAndReadHeadAsync(cancellationToken).ConfigureAwait(false) != _state.Descriptor.Head)
                await RecoverAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<long> RepairPartialTailAndReadHeadAsync(CancellationToken cancellationToken)
        {
            var journalPath = _store.GetJournalPath(_key);
            if (!Directory.Exists(journalPath))
                return 0;
            var lastSegment = Directory.EnumerateFiles(journalPath, "segment-*.events")
                .OrderBy(path => path, StringComparer.Ordinal)
                .LastOrDefault();
            if (lastSegment is null)
                return 0;

            var bytes = await File.ReadAllBytesAsync(lastSegment, cancellationToken).ConfigureAwait(false);
            if (bytes.Length > 0 && bytes[^1] != (byte)'\n')
            {
                var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
                await using var stream = new FileStream(lastSegment, FileMode.Open, FileAccess.Write, FileShare.Read);
                stream.SetLength(lastNewline + 1L);
                stream.Flush(_store._options.FlushToDiskOnCommit);
            }

            long head = 0;
            var snapshot = new SegmentSnapshot(
                lastSegment,
                ParseSegmentStart(lastSegment),
                long.MaxValue,
                new FileInfo(lastSegment).Length);
            var generation = await ReadJournalGenerationAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var evt in _store.ReadSegmentEventsAsync(_key, generation, snapshot, cancellationToken).ConfigureAwait(false))
                head = evt.ThreadSequenceNumber;
            return head;
        }

        private async ValueTask LoadIndexAsync(CancellationToken cancellationToken)
        {
            var path = _store.GetIndexPath(_key);
            if (!File.Exists(path))
                return;
            foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3 || !long.TryParse(parts[0], out _))
                    throw new InvalidDataException($"Journal index '{path}' contains an invalid record.");
                _eventIds.Add(parts[1]);
            }
        }

        private async ValueTask RecoverAsync(CancellationToken cancellationToken)
        {
            _eventIds.Clear();
            _messageIds.Clear();
            var generation = await ReadJournalGenerationAsync(cancellationToken).ConfigureAwait(false);
            ThreadDescriptor? descriptor = null;
            var index = new StringBuilder();
            long currentStart = 1;
            var currentCount = 0;
            var files = Directory.EnumerateFiles(_store.GetJournalPath(_key), "segment-*.events").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            foreach (var path in files)
            {
                currentStart = ParseSegmentStart(path);
                currentCount = 0;
                var snapshot = new SegmentSnapshot(path, currentStart, long.MaxValue, new FileInfo(path).Length);
                await foreach (var evt in _store.ReadSegmentEventsAsync(_key, generation, snapshot, cancellationToken).ConfigureAwait(false))
                {
                    var expected = (descriptor?.Head ?? 0) + 1;
                    if (evt.ThreadSequenceNumber != expected || !_eventIds.Add(evt.EventId))
                        throw new InvalidDataException($"Journal '{path}' has non-contiguous positions or duplicate EventIds.");
                    descriptor = ThreadDescriptorProjection.Apply(_key, descriptor, _messageIds, evt, generation, expected);
                    index.Append(expected).Append('\t').Append(evt.EventId).Append('\t').Append(currentStart).Append('\n');
                    currentCount++;
                }
            }
            _state = descriptor is null ? null : new FileThreadDescriptorState(
                DescriptorSchema, DescriptorVersion, descriptor, _messageIds.ToArray(), currentStart, currentCount);
            if (_state is not null)
            {
                await EnsureJournalGenerationAsync(generation, cancellationToken).ConfigureAwait(false);
                await _store.WriteAtomicallyAsync(_store.GetIndexPath(_key), index.ToString(), cancellationToken).ConfigureAwait(false);
                await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask<long> ReadJournalGenerationAsync(CancellationToken cancellationToken)
        {
            var path = _store.GetJournalGenerationPath(_key);
            if (!File.Exists(path))
                return 1;

            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (!long.TryParse(
                    text,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var generation) || generation <= 0)
            {
                throw new InvalidDataException($"Journal generation '{path}' is invalid.");
            }

            return generation;
        }

        private async ValueTask EnsureJournalGenerationAsync(
            long generation,
            CancellationToken cancellationToken)
        {
            var path = _store.GetJournalGenerationPath(_key);
            if (File.Exists(path))
            {
                var existing = await ReadJournalGenerationAsync(cancellationToken).ConfigureAwait(false);
                if (existing != generation)
                {
                    throw new InvalidDataException(
                        $"Journal generation '{path}' contains {existing}, but the thread descriptor contains {generation}.");
                }
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await _store.WriteAtomicallyAsync(
                path,
                generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask SaveStateAsync(CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(_state, SessionJsonContext.Combined.FileThreadDescriptorState);
            await _store.WriteAtomicallyAsync(_store.GetDescriptorPath(_key), json, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask AppendTextAsync(string path, string text, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes(text);
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024, true);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (_store._options.FlushToDiskOnCommit)
                stream.Flush(true);
        }

        private void ThrowIfDeleted()
        {
            if (_deleted)
                throw new ThreadDeletedException(_key);
        }

        private static long ParseSegmentStart(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return long.TryParse(name.AsSpan("segment-".Length), out var start)
                ? start
                : throw new InvalidDataException($"Journal segment '{path}' has an invalid name.");
        }

        private static TaskCompletionSource NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record SegmentSnapshot(string Path, long FirstSequence, long LastSequence, long Length);
    private sealed record ReadBoundary(long Generation, long Through, IReadOnlyList<SegmentSnapshot> Segments);
    private sealed record ObservationSnapshot(ThreadJournalCursor Head, Task CommitSignal);
}

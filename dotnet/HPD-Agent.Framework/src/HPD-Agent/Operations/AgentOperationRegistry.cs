using System.Collections.Concurrent;

namespace HPD.Agent;

/// <summary>
/// Owns all live operation aggregates for one agent and commits registration before publication.
/// </summary>
internal sealed class AgentOperationRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, AgentOperation> _operations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AgentOperationTombstone> _tombstones =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _retiredOperationIds =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _registrationLock = new(1, 1);
    private readonly IAgentOperationEventSink _events;
    private readonly AgentOperationRetentionPolicy _retention;
    private int _disposed;

    /// <summary>Creates an operation registry backed by the canonical event sink.</summary>
    /// <param name="events">The owning thread-journal event sink.</param>
    internal AgentOperationRegistry(
        IAgentOperationEventSink events,
        AgentOperationRetentionPolicy? retention = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _retention = retention ?? new AgentOperationRetentionPolicy();
        _retention.Validate();
    }

    /// <summary>Registers and publishes one new operation after its journal fact commits.</summary>
    internal async ValueTask<AgentOperation> RegisterAsync(
        AgentOperationSnapshot initial,
        IAgentOperationController? controller = null,
        IAsyncDisposable? observer = null,
        IAsyncDisposable? executionOwner = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(initial);
        await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_operations.ContainsKey(initial.OperationId) || _retiredOperationIds.ContainsKey(initial.OperationId))
                throw new InvalidOperationException($"Operation '{initial.OperationId}' is already registered.");

            var operation = new AgentOperation(initial, _events, controller, observer, executionOwner);
            try
            {
                await _events.AppendAsync(new AgentOperationRegisteredEvent
                {
                    TraceId = initial.Invocation?.TraceId,
                    SessionId = initial.Address.SessionId,
                    ThreadId = initial.Address.ThreadId,
                    ThreadExecutionId = initial.OriginatingThreadExecutionId,
                    Operation = initial
                }, cancellationToken).ConfigureAwait(false);
                if (!_operations.TryAdd(initial.OperationId, operation))
                    throw new InvalidOperationException($"Operation '{initial.OperationId}' is already registered.");
                return operation;
            }
            catch
            {
                await operation.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    /// <summary>Looks up a live operation by HPD-authoritative identifier.</summary>
    internal bool TryGet(string operationId, out AgentOperation? operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        return _operations.TryGetValue(operationId, out operation);
    }

    /// <summary>Returns a point-in-time immutable projection of all live operations.</summary>
    internal IReadOnlyList<AgentOperationSnapshot> Snapshot() => _operations.Values
        .Select(static operation => operation.Snapshot)
        .OrderBy(static operation => operation.RegisteredAt)
        .ThenBy(static operation => operation.OperationId, StringComparer.Ordinal)
        .ToArray();

    internal IReadOnlyList<AgentOperation> LiveOperations() => _operations.Values.ToArray();

    internal async ValueTask<AgentOperationSnapshot> TransitionAsync(
        string operationId,
        AgentOperationTransition transition,
        CancellationToken cancellationToken)
    {
        var operation = GetRequired(operationId);
        while (true)
        {
            try
            {
                return (await operation.TransitionAsync(
                    transition, operation.Snapshot.Version, cancellationToken).ConfigureAwait(false)).Snapshot;
            }
            catch (AgentOperationVersionConflictException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    internal IReadOnlyList<AgentOperationTombstone> Tombstones() => _tombstones.Values
        .OrderBy(static tombstone => tombstone.FinishedAt)
        .ThenBy(static tombstone => tombstone.OperationId, StringComparer.Ordinal)
        .ToArray();

    internal async ValueTask RehydrateAsync(IEnumerable<AgentEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var snapshots = new Dictionary<string, AgentOperationSnapshot>(StringComparer.Ordinal);
        var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var evt in events.OrderBy(static evt => evt.ThreadSequenceNumber))
        {
            switch (evt)
            {
                case AgentOperationRegisteredEvent registered:
                    if (_retiredOperationIds.ContainsKey(registered.Operation.OperationId))
                        break;
                    snapshots[registered.Operation.OperationId] = registered.Operation;
                    break;
                case AgentOperationTransitionedEvent transitioned:
                    if (_retiredOperationIds.ContainsKey(transitioned.OperationId))
                        break;
                    if (!snapshots.TryGetValue(transitioned.OperationId, out var current) ||
                        transitioned.Operation.Version > current.Version)
                        snapshots[transitioned.OperationId] = transitioned.Operation;
                    if (transitioned.ProviderDeduplicationKey is { Length: > 0 } key)
                    {
                        if (!keys.TryGetValue(transitioned.OperationId, out var operationKeys))
                            keys[transitioned.OperationId] = operationKeys = new(StringComparer.Ordinal);
                        operationKeys.Add(key);
                    }
                    break;
                case AgentOperationTombstonedEvent tombstoned:
                    snapshots.Remove(tombstoned.Tombstone.OperationId);
                    _retiredOperationIds[tombstoned.Tombstone.OperationId] = 0;
                    if (_operations.TryRemove(tombstoned.Tombstone.OperationId, out var compacted))
                        await compacted.DisposeAsync().ConfigureAwait(false);
                    _tombstones[tombstoned.Tombstone.OperationId] = tombstoned.Tombstone;
                    break;
                case AgentOperationTombstoneEvictedEvent evicted:
                    _retiredOperationIds[evicted.OperationId] = 0;
                    _tombstones.TryRemove(evicted.OperationId, out _);
                    break;
            }
        }

        foreach (var snapshot in snapshots.Values)
        {
            if (_tombstones.ContainsKey(snapshot.OperationId) || _operations.ContainsKey(snapshot.OperationId))
                continue;
            var materialized = IsTerminal(snapshot.ProviderStatus)
                ? snapshot
                : snapshot with { ObservationStatus = AgentOperationObservationStatus.Detached };
            _operations.TryAdd(snapshot.OperationId, new AgentOperation(
                materialized,
                _events,
                providerDeduplicationKeys: keys.GetValueOrDefault(snapshot.OperationId)));
        }
    }

    internal async ValueTask CompactAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var terminalByThread = _operations.Values
            .Where(static operation => IsTerminal(operation.Snapshot.ProviderStatus))
            .GroupBy(static operation => (operation.Snapshot.Address.SessionId, operation.Snapshot.Address.ThreadId));
        foreach (var group in terminalByThread)
        {
            var ordered = group.OrderByDescending(static operation => operation.Snapshot.FinishedAt).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var operation = ordered[index];
                var snapshot = operation.Snapshot;
                if (index < _retention.MaximumTerminalOperationsPerThread &&
                    now - snapshot.FinishedAt!.Value <= _retention.TerminalRetention)
                    continue;
                var tombstone = new AgentOperationTombstone
                {
                    OperationId = snapshot.OperationId,
                    Address = snapshot.Address,
                    ProviderDeduplicationKeys = operation.ProviderDeduplicationKeys,
                    ProviderStatus = snapshot.ProviderStatus,
                    FinishedAt = snapshot.FinishedAt.GetValueOrDefault(snapshot.UpdatedAt),
                    FinalVersion = snapshot.Version
                };
                await _events.AppendAsync(new AgentOperationTombstonedEvent
                {
                    Tombstone = tombstone,
                    SessionId = snapshot.Address.SessionId,
                    ThreadId = snapshot.Address.ThreadId,
                    ThreadExecutionId = snapshot.OriginatingThreadExecutionId
                }, cancellationToken).ConfigureAwait(false);
                if (_operations.TryRemove(snapshot.OperationId, out var removed))
                    await removed.DisposeAsync().ConfigureAwait(false);
                _retiredOperationIds[snapshot.OperationId] = 0;
                _tombstones[snapshot.OperationId] = tombstone;
            }
        }

        foreach (var tombstone in _tombstones.Values)
        {
            if (now - tombstone.FinishedAt <= _retention.ProviderDeduplicationRetention)
                continue;
            if (!_tombstones.TryRemove(tombstone.OperationId, out _))
                continue;
            await _events.AppendAsync(new AgentOperationTombstoneEvictedEvent
            {
                OperationId = tombstone.OperationId,
                EvictedAt = now,
                SessionId = tombstone.Address.SessionId,
                ThreadId = tombstone.Address.ThreadId
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTerminal(AgentOperationProviderStatus status) => status is
        AgentOperationProviderStatus.Completed or AgentOperationProviderStatus.Failed or
        AgentOperationProviderStatus.Cancelled;

    internal async ValueTask RequestCancellationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        var operation = GetRequired(operationId);
        if ((operation.Snapshot.Control.Capabilities & AgentOperationCapabilities.Cancel) == 0 ||
            operation.Controller is null)
            throw new InvalidOperationException($"Operation '{operationId}' does not support cancellation.");
        await operation.Controller.RequestCancellationAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask SupplyInputAsync(
        string operationId,
        AgentOperationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var operation = GetRequired(operationId);
        if ((operation.Snapshot.Control.Capabilities & AgentOperationCapabilities.Update) == 0 ||
            operation.Controller is null)
            throw new InvalidOperationException($"Operation '{operationId}' does not support input updates.");
        await operation.Controller.SupplyInputAsync(input, cancellationToken).ConfigureAwait(false);
    }

    private AgentOperation GetRequired(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        return _operations.TryGetValue(operationId, out var operation)
            ? operation
            : throw new KeyNotFoundException($"Operation '{operationId}' is not known to this agent.");
    }

    /// <summary>Rejects new work, drains operations, then cancels local work and detaches remote observation.</summary>
    internal async ValueTask ShutdownAsync(AgentShutdownOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
        List<Exception>? failures = null;
        await _registrationLock.WaitAsync().ConfigureAwait(false);
        try
        {

        await WaitForLocalTerminalAsync(options.GracefulDrainTimeout).ConfigureAwait(false);
        var active = _operations.Values.Where(static operation => !IsTerminal(operation.Snapshot.ProviderStatus)).ToArray();
        foreach (var operation in active)
        {
            var snapshot = operation.Snapshot;
            var remote = snapshot.SourceKind is AgentOperationSourceKind.McpTask or AgentOperationSourceKind.ProviderOperation;
            if (remote)
            {
                if (options.RemoteOperations == AgentRemoteOperationShutdownPolicy.RequestCancellation &&
                    operation.Controller is not null &&
                    (snapshot.Control.Capabilities & AgentOperationCapabilities.Cancel) != 0)
                {
                    try { await operation.Controller.RequestCancellationAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch { }
                }
                try
                {
                    await TryTransitionLatestAsync(operation, new AgentOperationTransition
                    {
                        ObservationStatus = AgentOperationObservationStatus.Detached,
                        ProviderDeduplicationKey = $"shutdown-detached:{snapshot.OperationId}"
                    }).ConfigureAwait(false);
                }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }
            else if (operation.Controller is not null &&
                (snapshot.Control.Capabilities & AgentOperationCapabilities.Cancel) != 0)
            {
                try { await operation.Controller.RequestCancellationAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
        }

        await WaitForLocalTerminalAsync(options.CancellationDrainTimeout).ConfigureAwait(false);
        foreach (var operation in _operations.Values)
        {
            var snapshot = operation.Snapshot;
            var remote = snapshot.SourceKind is AgentOperationSourceKind.McpTask or AgentOperationSourceKind.ProviderOperation;
            if (!remote && !IsTerminal(snapshot.ProviderStatus))
            {
                try
                {
                    await TryTransitionLatestAsync(operation, new AgentOperationTransition
                    {
                        ProviderStatus = AgentOperationProviderStatus.Failed,
                        ObservationStatus = AgentOperationObservationStatus.Stopped,
                        Failure = new AgentOperationFailure(
                            "shutdown_deadline_exceeded",
                            "Local operation did not stop before the configured shutdown deadline."),
                        ProviderDeduplicationKey = $"shutdown-forced:{snapshot.OperationId}"
                    }).ConfigureAwait(false);
                }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }

            try { await operation.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        _operations.Clear();
        }
        finally
        {
            _registrationLock.Release();
            _registrationLock.Dispose();
        }
        if (failures is { Count: > 0 })
            throw new AggregateException("One or more Agent operations failed during shutdown.", failures);
    }

    private async ValueTask WaitForLocalTerminalAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (_operations.Values.Any(static operation =>
            operation.Snapshot.SourceKind is not AgentOperationSourceKind.McpTask and not AgentOperationSourceKind.ProviderOperation &&
            !IsTerminal(operation.Snapshot.ProviderStatus)) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
    }

    private static async ValueTask TryTransitionLatestAsync(
        AgentOperation operation,
        AgentOperationTransition transition)
    {
        while (true)
        {
            try
            {
                await operation.TransitionAsync(transition, operation.Snapshot.Version, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }
            catch (AgentOperationVersionConflictException) { }
            catch (InvalidOperationException) { return; }
        }
    }

    /// <summary>Stops observers before controllers and rejects further registrations.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await _registrationLock.WaitAsync().ConfigureAwait(false);
        List<Exception>? failures = null;
        try
        {
            foreach (var operation in _operations.Values)
            {
                try { await operation.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }
            _operations.Clear();
        }
        finally
        {
            _registrationLock.Release();
            _registrationLock.Dispose();
        }
        if (failures is { Count: > 0 })
            throw new AggregateException("One or more Agent operations failed to dispose.", failures);
    }
}

using System.Collections.Concurrent;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public interface IDebugSessionManager : IAsyncDisposable
{
    string RuntimeId { get; }
    bool IsAvailable { get; }
}

public sealed class DebugSessionOwnershipException(string reasonCode, string message) : InvalidOperationException(message)
{
    public string ReasonCode { get; } = reasonCode;
}

internal sealed record DebugTreeLookupScope(
    string AgentRuntimeRegistrationId,
    string SessionId,
    string ThreadId);

/// <summary>Bounded, non-live evidence retained after a debug tree terminates.</summary>
internal sealed record DebugTerminalRecord
{
    public required DebugTreeOwnership Ownership { get; init; }
    public required DebugSemanticStartKind SemanticStartKind { get; init; }
    public required DebugAdapterStartMethod AdapterStartMethod { get; init; }
    public required string AdapterId { get; init; }
    public required string FinalStatus { get; init; }
    public int? ExitCode { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required DebugBreakpointCounts Breakpoints { get; init; }
    public required DebugTreeSnapshot Snapshot { get; init; }
    public required DebugOutputSnapshot Output { get; init; }
    public required IReadOnlyList<DebugStoredArtifact> Artifacts { get; init; }
    public string? SafeReasonCode { get; init; }
}

/// <summary>Bounded model-result metadata for a completed debug tree.</summary>
/// <param name="DebugTreeId">Opaque debug-tree identity.</param>
/// <param name="FinalStatus">Final semantic tree status.</param>
/// <param name="ExitCode">Safe adapter-reported exit code when available.</param>
/// <param name="CompletedAt">Terminal-record completion time.</param>
/// <param name="Breakpoints">Truthful terminal breakpoint counts.</param>
/// <param name="RetainedOutputBytes">Bytes retained in the bounded output tail.</param>
/// <param name="DroppedOutputBytes">Output bytes dropped before retention.</param>
/// <param name="ArtifactCount">Bounded artifact-reference count.</param>
/// <param name="SafeReasonCode">Optional classified terminal reason.</param>
public sealed record DebugTerminalRecordMetadata(
    string DebugTreeId,
    string FinalStatus,
    int? ExitCode,
    DateTimeOffset CompletedAt,
    DebugBreakpointCounts Breakpoints,
    long RetainedOutputBytes,
    long DroppedOutputBytes,
    int ArtifactCount,
    string? SafeReasonCode);

internal static class DebugTerminalRecordMetadataProjection
{
    public static DebugTerminalRecordMetadata Project(DebugTerminalRecord record)
        => new(
            record.Ownership.DebugTreeId,
            record.FinalStatus,
            record.ExitCode,
            record.CompletedAt,
            record.Breakpoints,
            record.Output.RetainedBytes,
            record.Output.DroppedBytes,
            record.Artifacts.Count,
            record.SafeReasonCode);
}

internal sealed class DebugTreeReservation : IAsyncDisposable
{
    private readonly DebugSessionManager _manager;
    private int _settled;

    internal DebugTreeReservation(DebugSessionManager manager, string treeId, DebugTreeOwnership ownership)
    {
        _manager = manager;
        TreeId = treeId;
        Ownership = ownership;
    }

    public string TreeId { get; }
    public DebugTreeOwnership Ownership { get; }

    public void Commit(DebugSessionTree tree)
    {
        if (Interlocked.CompareExchange(ref _settled, 1, 0) != 0)
            throw new InvalidOperationException("The debug-tree reservation is already settled.");
        _manager.Commit(this, tree);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _settled, 1, 0) == 0)
            _manager.Rollback(this);
        return ValueTask.CompletedTask;
    }
}

internal sealed class DebugSessionManager : IDebugSessionManager
{
    private readonly ConcurrentDictionary<string, TreeEntry> _trees = new(StringComparer.Ordinal);
    private readonly IDebugTerminalRecordStore _terminals;
    private int _disposed;

    public DebugSessionManager(IDebugTerminalRecordStore terminals)
    {
        RuntimeId = Guid.NewGuid().ToString("N");
        _terminals = terminals ?? throw new ArgumentNullException(nameof(terminals));
    }

    public string RuntimeId { get; }
    public bool IsAvailable => Volatile.Read(ref _disposed) == 0;

    internal DebugTreeReservation ReserveTree(
        string ownerSessionId,
        string ownerThreadId,
        string environmentId,
        long environmentRevision,
        string? treeId = null)
    {
        ObjectDisposedException.ThrowIf(!IsAvailable, this);
        if (string.IsNullOrWhiteSpace(ownerSessionId) || string.IsNullOrWhiteSpace(ownerThreadId) || string.IsNullOrWhiteSpace(environmentId))
            throw new ArgumentException("Complete debug-tree ownership is required.");
        treeId ??= Guid.NewGuid().ToString("N");
        var ownership = new DebugTreeOwnership(RuntimeId, ownerSessionId, ownerThreadId, treeId, environmentId, environmentRevision);
        if (_terminals.TryGet(
                new(RuntimeId, ownerSessionId, ownerThreadId),
                treeId,
                out _) ||
            !_trees.TryAdd(treeId, new(ownership)))
            throw new InvalidOperationException($"Debug tree '{treeId}' already exists.");
        return new(this, treeId, ownership);
    }

    internal DebugSessionTree ResolveTree(DebugTreeLookupScope scope, string treeId)
    {
        ObjectDisposedException.ThrowIf(!IsAvailable, this);
        if (!_trees.TryGetValue(treeId, out var entry))
        {
            if (_terminals.TryGet(scope, treeId, out _))
                throw new InvalidOperationException(
                    $"Debug tree '{treeId}' has terminated and has no live protocol session.");
            throw new KeyNotFoundException($"Debug tree '{treeId}' is not live.");
        }
        if (!Matches(entry.Ownership, scope))
            throw new DebugSessionOwnershipException("SESSION_OWNERSHIP_MISMATCH", "The debug tree belongs to another runtime, session, or thread.");
        return entry.LiveTree ??
            throw new KeyNotFoundException($"Debug tree '{treeId}' is not live.");
    }

    internal bool TryResolveTerminal(
        DebugTreeLookupScope scope,
        string treeId,
        out DebugTerminalRecord terminal)
    {
        ObjectDisposedException.ThrowIf(!IsAvailable, this);
        return _terminals.TryGet(scope, treeId, out terminal);
    }

    internal IReadOnlyList<DebugSessionTree> ListTrees(DebugTreeLookupScope scope)
        => _trees.Values.Where(x => x.LiveTree is not null && Matches(x.Ownership, scope))
            .Select(x => x.LiveTree!).OrderBy(x => x.Ownership.DebugTreeId, StringComparer.Ordinal).ToArray();

    internal async ValueTask<bool> RemoveAndDisposeAsync(DebugTreeLookupScope scope, string treeId)
    {
        if (!_trees.TryGetValue(treeId, out var existing))
            return _terminals.Remove(scope, treeId);
        if (!Matches(existing.Ownership, scope))
            throw new DebugSessionOwnershipException("SESSION_OWNERSHIP_MISMATCH", "The debug tree belongs to another runtime, session, or thread.");
        if (existing.LiveTree is null)
            return false;
        await RetainAndDisposeAsync(
            scope,
            treeId,
            "Terminated",
            "TREE_DISPOSED").ConfigureAwait(false);
        return true;
    }

    internal async ValueTask<bool> DiscardAndDisposeAsync(
        DebugTreeLookupScope scope,
        string treeId)
    {
        if (!_trees.TryGetValue(treeId, out var existing))
            return _terminals.Remove(scope, treeId);
        if (!Matches(existing.Ownership, scope))
            throw new DebugSessionOwnershipException(
                "SESSION_OWNERSHIP_MISMATCH",
                "The debug tree belongs to another runtime, session, or thread.");
        if (!_trees.TryRemove(treeId, out var removed))
            return false;
        if (removed.LiveTree is not null)
            await removed.LiveTree.DisposeAsync().ConfigureAwait(false);
        return true;
    }

    internal async ValueTask RetainAndDisposeAsync(
        DebugTreeLookupScope scope,
        string treeId,
        string finalStatus,
        string safeReasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalStatus);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeReasonCode);
        if (!_trees.TryGetValue(treeId, out var current) || current.LiveTree is null)
            return;
        if (!Matches(current.Ownership, scope))
            throw new DebugSessionOwnershipException(
                "SESSION_OWNERSHIP_MISMATCH",
                "The terminal record belongs to another debug tree owner.");
        if (!_trees.TryRemove(new KeyValuePair<string, TreeEntry>(treeId, current)))
            return;
        var publisher = current.LiveTree.EventPublisher;
        await current.LiveTree.StopAndDrainOwnedResourcesAsync()
            .ConfigureAwait(false);
        var terminal = DebugTerminalRecordFactory.Create(
            current.LiveTree,
            finalStatus,
            safeReasonCode);
        _terminals.Retain(
            terminal,
            CreateEvictionObserver(publisher));
        await current.LiveTree.DisposeAsync().ConfigureAwait(false);
    }

    internal Task DisposeOwnedThreadAsync(string ownerSessionId, string ownerThreadId)
        => DisposeMatchingAsync(x => string.Equals(x.SessionId, ownerSessionId, StringComparison.Ordinal)
            && string.Equals(x.ThreadId, ownerThreadId, StringComparison.Ordinal));

    internal Task InvalidateEnvironmentAsync(string environmentId, long? environmentRevision = null)
        => DisposeMatchingAsync(x => string.Equals(x.EnvironmentId, environmentId, StringComparison.Ordinal)
            && (environmentRevision is null || x.EnvironmentRevision == environmentRevision));

    internal void Commit(DebugTreeReservation reservation, DebugSessionTree tree)
    {
        if (tree.Ownership != reservation.Ownership)
            throw new InvalidOperationException("The debug tree does not match its reservation ownership.");
        while (true)
        {
            if (!_trees.TryGetValue(reservation.TreeId, out var current) || current.Ownership != reservation.Ownership)
                throw new InvalidOperationException("The debug-tree reservation no longer exists.");
            if (current.LiveTree is not null)
                throw new InvalidOperationException("The debug tree is already committed.");
            if (_trees.TryUpdate(reservation.TreeId, current with { LiveTree = tree }, current)) return;
        }
    }

    internal void Rollback(DebugTreeReservation reservation)
    {
        if (_trees.TryGetValue(reservation.TreeId, out var current) && current.LiveTree is null && current.Ownership == reservation.Ownership)
            _trees.TryRemove(new KeyValuePair<string, TreeEntry>(reservation.TreeId, current));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var entries = _trees.ToArray();
        _trees.Clear();
        _terminals.Clear();
        foreach (var entry in entries)
            if (entry.Value.LiveTree is not null)
                try { await entry.Value.LiveTree.DisposeAsync().ConfigureAwait(false); } catch { }
    }

    private static bool Matches(DebugTreeOwnership ownership, DebugTreeLookupScope scope)
        => string.Equals(ownership.AgentRuntimeRegistrationId, scope.AgentRuntimeRegistrationId, StringComparison.Ordinal)
        && string.Equals(ownership.SessionId, scope.SessionId, StringComparison.Ordinal)
        && string.Equals(ownership.ThreadId, scope.ThreadId, StringComparison.Ordinal);

    private async Task DisposeMatchingAsync(Func<DebugTreeOwnership, bool> predicate)
    {
        foreach (var pair in _trees.ToArray())
        {
            if (!predicate(pair.Value.Ownership) || !_trees.TryRemove(pair)) continue;
            if (pair.Value.LiveTree is not null)
            {
                var publisher = pair.Value.LiveTree.EventPublisher;
                try
                {
                    await pair.Value.LiveTree.StopAndDrainOwnedResourcesAsync()
                        .ConfigureAwait(false);
                }
                catch { }
                var terminal = DebugTerminalRecordFactory.Create(
                    pair.Value.LiveTree,
                    "Invalidated",
                    "RUNTIME_INVALIDATED");
                _terminals.Retain(
                    terminal,
                    CreateEvictionObserver(publisher));
                try { await pair.Value.LiveTree.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }
    }

    private sealed record TreeEntry(
        DebugTreeOwnership Ownership,
        DebugSessionTree? LiveTree = null);

    private static Action<DebugTerminalRecord, string>? CreateEvictionObserver(
        ITreeDebugEventPublisher? publisher)
    {
        if (publisher is null)
            return null;
        return (record, reason) =>
        {
            _ = PublishEvictionAsync(publisher, record, reason);
        };
    }

    private static async Task PublishEvictionAsync(
        ITreeDebugEventPublisher publisher,
        DebugTerminalRecord record,
        string reason)
    {
        try
        {
            await publisher.PublishAsync(new DebugTerminalRecordEvictedEvent
            {
                DebugTreeId = record.Ownership.DebugTreeId,
                DebugSessionId = record.Snapshot.ActiveDebugSessionId ??
                    record.Snapshot.Sessions.FirstOrDefault()?.DebugSessionId ??
                    "terminal",
                AdapterId = record.AdapterId,
                SafeReasonCode = reason
            }, durable: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Terminal eviction must remain bounded even if event persistence fails.
        }
    }
}

internal static class DebugTerminalRecordFactory
{
    public static DebugTerminalRecord Create(
        DebugSessionTree tree,
        string finalStatus,
        string safeReasonCode)
    {
        var session = tree.Sessions.TryGetValue(tree.RootSessionId, out var root)
            ? root
            : tree.Sessions.Values.OrderBy(value => value.CreatedAt).FirstOrDefault();
        var desired = tree.Breakpoints.Snapshot;
        var states = session?.AdapterBreakpoints.Snapshot ?? [];
        var requested = desired.Source.Length + desired.Function.Length +
            desired.Exception.Length + desired.Instruction.Length + desired.Data.Length;
        var verified = states.Count(state => state.Verified);
        var output = session?.Output.Snapshot(includeTelemetry: false) ??
            new DebugOutputSnapshot([], 0, 0, 0, 0, 0);
        var sessionId = session?.SessionId ?? tree.RootSessionId;
        var retained = new List<DebugOutputRecord>();
        var retainedBytes = 0;
        foreach (var record in output.Records.Reverse())
        {
            if (retainedBytes + record.Utf8Bytes > 16 * 1024)
                break;
            retained.Add(record with
            {
                VariablesToken = null,
                LocationToken = null
            });
            retainedBytes += record.Utf8Bytes;
        }
        retained.Reverse();
        long hostDroppedBytes = 0;
        foreach (var host in tree.OwnedResources
                     .OfType<DebugOwnedProcessResource>())
        {
            var hostOutput = host.OutputSnapshot;
            hostDroppedBytes += hostOutput.DroppedBytes;
            foreach (var (category, text) in new[]
                     {
                         (DebugOutputCategory.StandardOutput, hostOutput.Stdout),
                         (DebugOutputCategory.StandardError, hostOutput.Stderr)
                     })
            {
                if (string.IsNullOrEmpty(text))
                    continue;
                var remaining = (16 * 1024) - retainedBytes;
                if (remaining <= 0)
                {
                    hostDroppedBytes += System.Text.Encoding.UTF8.GetByteCount(text);
                    continue;
                }
                var boundedText = BoundUtf8(text, remaining, out var dropped);
                hostDroppedBytes += dropped;
                var bytes = System.Text.Encoding.UTF8.GetByteCount(boundedText);
                retained.Add(new(
                    tree.Ownership.DebugTreeId,
                    sessionId,
                    long.MaxValue - retained.Count,
                    DateTimeOffset.UtcNow,
                    category == DebugOutputCategory.StandardOutput
                        ? "stdout"
                        : "stderr",
                    category,
                    "owned-host",
                    boundedText,
                    bytes,
                    0,
                    hostOutput.DroppedBytes + dropped,
                    hostOutput.DroppedBytes + dropped > 0,
                    null,
                    null,
                    null,
                    null,
                    null));
                retainedBytes += bytes;
            }
        }
        return new DebugTerminalRecord
        {
            Ownership = tree.Ownership,
            SemanticStartKind = tree.Authorization.SemanticStartKind,
            AdapterStartMethod =
                session?.AdapterStartMethod ?? tree.Authorization.AdapterStartMethod,
            AdapterId = session?.AdapterPlan.AdapterId ?? tree.Authorization.AdapterId,
            FinalStatus = finalStatus,
            ExitCode = session?.ExitCode,
            StartedAt = session?.CreatedAt ?? DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Breakpoints = new(
                requested,
                states.Length,
                verified,
                Math.Max(0, requested - verified)),
            Snapshot = DebugSnapshotProjector.Project(tree),
            Output = output with
            {
                Records = retained,
                RetainedBytes = retainedBytes,
                DroppedBytes = output.DroppedBytes + hostDroppedBytes
            },
            Artifacts = tree.StoredArtifacts.Take(128).ToArray(),
            SafeReasonCode = safeReasonCode
        };
    }

    private static string BoundUtf8(
        string value,
        int maximumBytes,
        out int droppedBytes)
    {
        var originalBytes = System.Text.Encoding.UTF8.GetByteCount(value);
        if (originalBytes <= maximumBytes)
        {
            droppedBytes = 0;
            return value;
        }
        var start = value.Length;
        var retainedBytes = 0;
        while (start > 0)
        {
            var previous = start - 1;
            if (previous > 0 &&
                char.IsLowSurrogate(value[previous]) &&
                char.IsHighSurrogate(value[previous - 1]))
                previous--;
            var candidateBytes = System.Text.Encoding.UTF8.GetByteCount(
                value.AsSpan(previous, start - previous));
            if (retainedBytes + candidateBytes > maximumBytes)
                break;
            retainedBytes += candidateBytes;
            start = previous;
        }
        droppedBytes = originalBytes - retainedBytes;
        return value[start..];
    }
}

using System.Collections.Concurrent;

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

public sealed class DebugSessionManager : IDebugSessionManager
{
    private readonly ConcurrentDictionary<string, TreeEntry> _trees = new(StringComparer.Ordinal);
    private int _disposed;

    public DebugSessionManager() => RuntimeId = Guid.NewGuid().ToString("N");

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
        if (!_trees.TryAdd(treeId, new(ownership)))
            throw new InvalidOperationException($"Debug tree '{treeId}' already exists.");
        return new(this, treeId, ownership);
    }

    internal DebugSessionTree ResolveTree(DebugTreeLookupScope scope, string treeId)
    {
        ObjectDisposedException.ThrowIf(!IsAvailable, this);
        if (!_trees.TryGetValue(treeId, out var entry))
            throw new KeyNotFoundException($"Debug tree '{treeId}' is not live.");
        if (!Matches(entry.Ownership, scope))
            throw new DebugSessionOwnershipException("SESSION_OWNERSHIP_MISMATCH", "The debug tree belongs to another runtime, session, or thread.");
        return entry.LiveTree ?? throw new KeyNotFoundException($"Debug tree '{treeId}' is not live.");
    }

    internal IReadOnlyList<DebugSessionTree> ListTrees(DebugTreeLookupScope scope)
        => _trees.Values.Where(x => x.LiveTree is not null && Matches(x.Ownership, scope))
            .Select(x => x.LiveTree!).OrderBy(x => x.Ownership.DebugTreeId, StringComparer.Ordinal).ToArray();

    internal async ValueTask<bool> RemoveAndDisposeAsync(DebugTreeLookupScope scope, string treeId)
    {
        if (!_trees.TryGetValue(treeId, out var existing)) return false;
        if (!Matches(existing.Ownership, scope))
            throw new DebugSessionOwnershipException("SESSION_OWNERSHIP_MISMATCH", "The debug tree belongs to another runtime, session, or thread.");
        if (!_trees.TryRemove(treeId, out var removed)) return false;
        if (removed.LiveTree is not null) await removed.LiveTree.DisposeAsync().ConfigureAwait(false);
        return true;
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
            if (current.LiveTree is not null) throw new InvalidOperationException("The debug tree is already live.");
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
                try { await pair.Value.LiveTree.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    private sealed record TreeEntry(DebugTreeOwnership Ownership, DebugSessionTree? LiveTree = null);
}

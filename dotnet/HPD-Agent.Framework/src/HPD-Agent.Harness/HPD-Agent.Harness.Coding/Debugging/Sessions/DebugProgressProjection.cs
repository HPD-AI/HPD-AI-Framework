using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugProgressSnapshot(
    string ProgressId,
    string Title,
    string? Message,
    double? Percentage,
    int? RequestId,
    bool Cancellable,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    bool CancellationRequested);

internal sealed class DebugProgressProjection : IDisposable
{
    private const int MaximumEntries = 128;
    private readonly object _gate = new();
    private readonly Dictionary<string, DebugProgressSnapshot> _entries = new(StringComparer.Ordinal);
    private readonly Timer _expiryTimer;
    private int _disposed;

    public DebugProgressProjection()
        => _expiryTimer = new(static state => ((DebugProgressProjection)state!).ExpireOrphans(),
            this, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    public IReadOnlyList<DebugProgressSnapshot> Snapshot
    {
        get { lock (_gate) return _entries.Values.OrderBy(x => x.StartedAt).ToArray(); }
    }

    public DebugProgressSnapshot Start(ProgressStartEventBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(body.ProgressId);
        lock (_gate)
        {
            CleanupLocked(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5));
            if (!_entries.ContainsKey(body.ProgressId) && _entries.Count >= MaximumEntries)
                _entries.Remove(_entries.OrderBy(x => x.Value.UpdatedAt).First().Key);
            var now = DateTimeOffset.UtcNow;
            return _entries[body.ProgressId] = new(body.ProgressId, Bound(body.Title, 1024)!,
                Bound(body.Message, 4096), Clamp(body.Percentage), body.RequestId, body.Cancellable == true,
                now, now, false);
        }
    }

    public DebugProgressSnapshot? Update(ProgressUpdateEventBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_gate)
        {
            CleanupLocked(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5));
            if (!_entries.TryGetValue(body.ProgressId, out var current)) return null;
            return _entries[body.ProgressId] = current with
            {
                Message = body.Message is null ? current.Message : Bound(body.Message, 4096),
                Percentage = body.Percentage is null ? current.Percentage : Clamp(body.Percentage),
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public DebugProgressSnapshot? End(ProgressEndEventBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_gate)
        {
            if (!_entries.Remove(body.ProgressId, out var current)) return null;
            return current with { Message = body.Message is null ? current.Message : Bound(body.Message, 4096), UpdatedAt = DateTimeOffset.UtcNow };
        }
    }

    public bool MarkCancellationRequested(string progressId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(progressId, out var current) || !current.Cancellable) return false;
            _entries[progressId] = current with { CancellationRequested = true, UpdatedAt = DateTimeOffset.UtcNow };
            return true;
        }
    }

    public void Clear() { lock (_gate) _entries.Clear(); }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _expiryTimer.Dispose();
        Clear();
    }

    private void ExpireOrphans()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_gate) CleanupLocked(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5));
    }

    internal void ExpireBefore(DateTimeOffset cutoff)
    {
        lock (_gate) CleanupLocked(cutoff);
    }

    private void CleanupLocked(DateTimeOffset cutoff)
    {
        foreach (var id in _entries.Where(x => x.Value.UpdatedAt < cutoff).Select(x => x.Key).ToArray())
            _entries.Remove(id);
    }

    private static double? Clamp(double? value)
        => value is null ? null : double.IsFinite(value.Value) ? Math.Clamp(value.Value, 0, 100) : null;
    private static string? Bound(string? value, int maximum)
        => value is null ? null : value[..Math.Min(value.Length, maximum)];
}

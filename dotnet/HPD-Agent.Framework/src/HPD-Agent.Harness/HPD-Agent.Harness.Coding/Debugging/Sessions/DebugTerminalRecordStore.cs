using System.Collections.Concurrent;
using System.Text;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Runtime-scoped bounded storage for completed debug-tree evidence.</summary>
internal interface IDebugTerminalRecordStore
{
    void Retain(
        DebugTerminalRecord record,
        Action<DebugTerminalRecord, string>? onEvicted = null);

    bool TryGet(
        DebugTreeLookupScope owner,
        string debugTreeId,
        out DebugTerminalRecord record);

    bool Remove(DebugTreeLookupScope owner, string debugTreeId);

    void Clear();
}

/// <summary>Host policy for bounded terminal-debug evidence.</summary>
public sealed record DebugTerminalRecordStoreOptions
{
    public int MaximumRecords { get; init; } = 64;
    public long MaximumAggregateBytes { get; init; } = 1024 * 1024;
    public TimeSpan Retention { get; init; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Retains owner-bound terminal records with deterministic age, count, and byte eviction.
/// </summary>
internal sealed class DebugTerminalRecordStore : IDebugTerminalRecordStore
{
    private readonly DebugTerminalRecordStoreOptions _options;
    private readonly ConcurrentDictionary<string, Entry> _records =
        new(StringComparer.Ordinal);

    public DebugTerminalRecordStore(DebugTerminalRecordStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.MaximumRecords <= 0 ||
            options.MaximumAggregateBytes <= 0 ||
            options.Retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Terminal record bounds must be positive.");
    }

    public void Retain(
        DebugTerminalRecord record,
        Action<DebugTerminalRecord, string>? onEvicted = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        EvictExpired();
        var entry = new Entry(record, onEvicted);
        if (_records.TryGetValue(record.Ownership.DebugTreeId, out var previous))
            Notify(previous, "REPLACED");
        _records[record.Ownership.DebugTreeId] = entry;
        EvictToBounds();
    }

    public bool TryGet(
        DebugTreeLookupScope owner,
        string debugTreeId,
        out DebugTerminalRecord record)
    {
        EvictExpired();
        if (!_records.TryGetValue(debugTreeId, out var candidate))
        {
            record = null!;
            return false;
        }
        if (!Matches(candidate.Record.Ownership, owner))
            throw new DebugSessionOwnershipException(
                "SESSION_OWNERSHIP_MISMATCH",
                "The debug tree belongs to another runtime, session, or thread.");
        record = candidate.Record;
        return true;
    }

    internal bool Contains(string debugTreeId)
    {
        EvictExpired();
        return _records.ContainsKey(debugTreeId);
    }

    public bool Remove(DebugTreeLookupScope owner, string debugTreeId)
    {
        if (!TryGet(owner, debugTreeId, out _))
            return false;
        return _records.TryRemove(debugTreeId, out _);
    }

    public void Clear() => _records.Clear();

    private void EvictExpired()
    {
        var threshold = DateTimeOffset.UtcNow - _options.Retention;
        foreach (var pair in _records.ToArray())
            if (pair.Value.Record.CompletedAt < threshold &&
                _records.TryRemove(pair))
                Notify(pair.Value, "EXPIRED");
    }

    private void EvictToBounds()
    {
        var ordered = _records
            .OrderBy(pair => pair.Value.Record.CompletedAt)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        var aggregate = ordered.Sum(pair => EstimateBytes(pair.Value.Record));
        var count = ordered.Length;
        foreach (var pair in ordered)
        {
            if (count <= _options.MaximumRecords &&
                aggregate <= _options.MaximumAggregateBytes)
                break;
            if (!_records.TryRemove(pair))
                continue;
            var reason = count > _options.MaximumRecords
                ? "COUNT_BOUND"
                : "BYTE_BOUND";
            count--;
            aggregate -= EstimateBytes(pair.Value.Record);
            Notify(pair.Value, reason);
        }
    }

    private static void Notify(Entry entry, string reason)
    {
        try { entry.OnEvicted?.Invoke(entry.Record, reason); }
        catch { }
    }

    internal static long EstimateBytes(DebugTerminalRecord record)
    {
        long total = 256;
        total += Utf8(record.Ownership.AgentRuntimeRegistrationId);
        total += Utf8(record.Ownership.SessionId);
        total += Utf8(record.Ownership.ThreadId);
        total += Utf8(record.Ownership.DebugTreeId);
        total += Utf8(record.Ownership.EnvironmentId);
        total += Utf8(record.AdapterId);
        total += Utf8(record.FinalStatus);
        total += Utf8(record.SafeReasonCode);
        total += Utf8(record.Snapshot.DebugTreeId);
        total += Utf8(record.Snapshot.ActiveDebugSessionId);
        total += Utf8(record.Snapshot.Status);
        foreach (var session in record.Snapshot.Sessions)
        {
            total += 128;
            total += Utf8(session.DebugSessionId);
            total += Utf8(session.ParentDebugSessionId);
            total += Utf8(session.AdapterId);
            total += Utf8(session.StopReason);
        }
        foreach (var output in record.Output.Records)
        {
            total += 128 + output.Utf8Bytes;
            total += Utf8(output.DebugTreeId);
            total += Utf8(output.DebugSessionId);
            total += Utf8(output.OriginalCategory);
            total += Utf8(output.Group);
            total += Utf8(output.SourcePath);
            total += Utf8(output.VariablesToken);
            total += Utf8(output.LocationToken);
        }
        foreach (var artifact in record.Artifacts)
        {
            total += 96;
            total += Utf8(artifact.Kind);
            total += Utf8(artifact.DebugSessionId);
            total += Utf8(artifact.ContentId);
            total += Utf8(artifact.Scope);
            total += Utf8(artifact.Version);
            foreach (var pair in artifact.Metadata)
                total += 16 + Utf8(pair.Key) + Utf8(pair.Value);
        }
        return total;
    }

    private static int Utf8(string? value)
        => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static bool Matches(
        DebugTreeOwnership ownership,
        DebugTreeLookupScope owner)
        => string.Equals(
                ownership.AgentRuntimeRegistrationId,
                owner.AgentRuntimeRegistrationId,
                StringComparison.Ordinal)
           && string.Equals(
                ownership.SessionId,
                owner.SessionId,
                StringComparison.Ordinal)
           && string.Equals(
                ownership.ThreadId,
                owner.ThreadId,
                StringComparison.Ordinal);

    private sealed record Entry(
        DebugTerminalRecord Record,
        Action<DebugTerminalRecord, string>? OnEvicted);
}

using System.Collections.Concurrent;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

public sealed record DebugAdapterDiagnosticRecord(
    string Reference,
    DateTimeOffset Timestamp,
    string AdapterId,
    string Phase,
    string? SafeExitReason,
    int? ExitCode,
    string StandardError,
    long DroppedChunks,
    long DroppedBytes);

public interface IDebugAdapterDiagnosticStore
{
    string Retain(string adapterId, string phase, DebugAdapterDiagnosticSnapshot snapshot);
    bool TryGet(string reference, out DebugAdapterDiagnosticRecord record);
}

public sealed class DebugAdapterDiagnosticStore : IDebugAdapterDiagnosticStore
{
    private const int MaximumRecords = 128;
    private readonly ConcurrentDictionary<string, DebugAdapterDiagnosticRecord> _records =
        new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();

    public string Retain(
        string adapterId,
        string phase,
        DebugAdapterDiagnosticSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(snapshot);
        var reference = $"debug-diagnostic-{Guid.NewGuid():N}";
        var stderr = snapshot.StandardError;
        if (stderr.Length > 64 * 1024)
            stderr = stderr[..(64 * 1024)];
        _records[reference] = new(
            reference,
            DateTimeOffset.UtcNow,
            adapterId,
            phase,
            snapshot.Exit?.SafeReasonCode,
            snapshot.Exit?.ExitCode,
            stderr,
            snapshot.DroppedChunks,
            snapshot.DroppedBytes);
        _order.Enqueue(reference);
        while (_records.Count > MaximumRecords && _order.TryDequeue(out var oldest))
            _records.TryRemove(oldest, out _);
        return reference;
    }

    public bool TryGet(string reference, out DebugAdapterDiagnosticRecord record)
        => _records.TryGetValue(reference, out record!);
}

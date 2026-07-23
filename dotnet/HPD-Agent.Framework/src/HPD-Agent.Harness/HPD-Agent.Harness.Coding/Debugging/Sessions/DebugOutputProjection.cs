using System.Text;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public enum DebugOutputCategory
{
    Console,
    StandardOutput,
    StandardError,
    Telemetry,
    Important,
    Unknown
}

internal sealed record DebugOutputRecord(
    string DebugTreeId,
    string DebugSessionId,
    long Sequence,
    DateTimeOffset Timestamp,
    string? OriginalCategory,
    DebugOutputCategory Category,
    string? Group,
    string Text,
    int Utf8Bytes,
    long DroppedRecordsBefore,
    long DroppedBytesBefore,
    bool Truncated,
    string? SourcePath,
    long? Line,
    long? Column,
    string? VariablesToken,
    string? LocationToken);

internal sealed record DebugOutputSnapshot(
    IReadOnlyList<DebugOutputRecord> Records,
    long OldestSequence,
    long NewestSequence,
    long RetainedBytes,
    long DroppedRecords,
    long DroppedBytes);

internal sealed class DebugOutputBuffer
{
    public const int DefaultMaximumRetainedBytes = 256 * 1024;
    public const int DefaultMaximumRecordBytes = DebugOutputEventCoalescer.MaximumLiveEventBytes;
    public const int DefaultMaximumRecords = 2048;
    private readonly object _gate = new();
    private readonly Queue<DebugOutputRecord> _records = [];
    private readonly int _maximumRetainedBytes;
    private readonly int _maximumRecordBytes;
    private readonly int _maximumRecords;
    private long _nextSequence;
    private long _retainedBytes;
    private long _droppedRecords;
    private long _droppedBytes;

    public DebugOutputBuffer(
        int maximumRetainedBytes = DefaultMaximumRetainedBytes,
        int maximumRecordBytes = DefaultMaximumRecordBytes,
        int maximumRecords = DefaultMaximumRecords)
    {
        if (maximumRetainedBytes <= 0 || maximumRecordBytes <= 0 || maximumRecordBytes > maximumRetainedBytes || maximumRecords <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedBytes));
        _maximumRetainedBytes = maximumRetainedBytes;
        _maximumRecordBytes = maximumRecordBytes;
        _maximumRecords = maximumRecords;
    }

    public DebugOutputRecord Append(
        string debugTreeId,
        string debugSessionId,
        OutputEventBody body,
        bool allowAnsi,
        string? variablesToken = null,
        string? locationToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(debugTreeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugSessionId);
        ArgumentNullException.ThrowIfNull(body);
        var normalized = Normalize(body.Category);
        var sanitized = DebugOutputSanitizer.Sanitize(body.Output, allowAnsi);
        var originalByteCount = Encoding.UTF8.GetByteCount(sanitized);
        var bounded = BoundUtf8(sanitized, _maximumRecordBytes, out var byteCount, out var truncated);
        lock (_gate)
        {
            if (truncated) _droppedBytes += originalByteCount - byteCount;
            while (_records.Count >= _maximumRecords || _retainedBytes + byteCount > _maximumRetainedBytes)
                DropOldestLocked();
            var record = new DebugOutputRecord(
                debugTreeId, debugSessionId,
                checked(++_nextSequence), DateTimeOffset.UtcNow, Bound(body.Category, 128), normalized,
                Bound(body.Group, 128), bounded, byteCount, _droppedRecords, _droppedBytes, truncated,
                Bound(body.Source?.Path, 4096), body.Line, body.Column, variablesToken,
                locationToken);
            _records.Enqueue(record);
            _retainedBytes += byteCount;
            return record;
        }
    }

    public DebugOutputSnapshot Snapshot(bool includeTelemetry = false)
        => Snapshot(fromSequence: null, toSequence: null, includeTelemetry: includeTelemetry);

    public DebugOutputSnapshot Snapshot(
        long? fromSequence,
        long? toSequence,
        bool includeTelemetry = false)
    {
        if (fromSequence is <= 0)
            throw new ArgumentOutOfRangeException(nameof(fromSequence), "Output sequence numbers begin at one.");
        if (toSequence is <= 0)
            throw new ArgumentOutOfRangeException(nameof(toSequence), "Output sequence numbers begin at one.");
        if (fromSequence is { } from && toSequence is { } to && from > to)
            throw new ArgumentException("The output range start must not exceed its end.", nameof(fromSequence));

        lock (_gate)
        {
            var oldestRetained = _records.TryPeek(out var oldest) ? oldest.Sequence : checked(_nextSequence + 1);
            if (fromSequence is { } requested && requested < oldestRetained && _droppedRecords > 0)
                throw new InvalidOperationException(
                    $"Output sequence {requested} is no longer retained; the oldest retained sequence is {oldestRetained}.");

            var records = _records.Where(x =>
                    (includeTelemetry || x.Category != DebugOutputCategory.Telemetry) &&
                    (fromSequence is null || x.Sequence >= fromSequence.Value) &&
                    (toSequence is null || x.Sequence <= toSequence.Value))
                .ToArray();
            return new(records, records.FirstOrDefault()?.Sequence ?? 0, records.LastOrDefault()?.Sequence ?? 0,
                records.Sum(x => (long)x.Utf8Bytes), _droppedRecords, _droppedBytes);
        }
    }

    private void DropOldestLocked()
    {
        var removed = _records.Dequeue();
        _retainedBytes -= removed.Utf8Bytes;
        _droppedRecords++;
        _droppedBytes += removed.Utf8Bytes;
    }

    private static DebugOutputCategory Normalize(string? category) => category switch
    {
        null or "console" => DebugOutputCategory.Console,
        "stdout" => DebugOutputCategory.StandardOutput,
        "stderr" => DebugOutputCategory.StandardError,
        "telemetry" => DebugOutputCategory.Telemetry,
        "important" => DebugOutputCategory.Important,
        _ => DebugOutputCategory.Unknown
    };

    private static string BoundUtf8(string value, int maximumBytes, out int bytes, out bool truncated)
    {
        var total = Encoding.UTF8.GetByteCount(value);
        if (total <= maximumBytes) { bytes = total; truncated = false; return value; }
        var builder = new StringBuilder(Math.Min(value.Length, maximumBytes));
        bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maximumBytes) break;
            builder.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        truncated = true;
        return builder.ToString();
    }

    private static string? Bound(string? value, int maximum)
        => value is null ? null : value[..Math.Min(value.Length, maximum)];
}

internal static class DebugOutputSanitizer
{
    public static string Sanitize(string value, bool allowAnsi)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '\u001b')
            {
                var recognized = index + 1 < value.Length && value[index + 1] is '[' or ']';
                var end = SkipEscape(value, index);
                if (allowAnsi && recognized) builder.Append(value, index, end - index + 1);
                index = end;
                continue;
            }
            if (current is '\n' or '\r' or '\t' || current >= ' ' && current != '\u007f')
                builder.Append(current);
        }
        return builder.ToString();
    }

    private static int SkipEscape(string value, int escapeIndex)
    {
        var next = escapeIndex + 1;
        if (next >= value.Length) return escapeIndex;
        if (value[next] != '[' && value[next] != ']') return next;
        for (var index = next + 1; index < value.Length; index++)
        {
            var current = value[index];
            if (value[next] == '[' && current is >= '@' and <= '~') return index;
            if (value[next] == ']' && (current == '\a' || current == '\u001b' && index + 1 < value.Length && value[index + 1] == '\\'))
                return current == '\u001b' ? index + 1 : index;
        }
        return value.Length - 1;
    }
}

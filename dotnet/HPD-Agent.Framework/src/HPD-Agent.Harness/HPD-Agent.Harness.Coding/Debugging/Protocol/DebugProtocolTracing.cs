using System.Buffers;
using System.Text.Json;
using HPD.Agent;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

public enum DebugProtocolTraceDirection { Inbound, Outbound }

/// <summary>Trusted-host-only diagnostic sink. It is never surfaced through debugger semantic APIs.</summary>
public interface IDebugProtocolTraceSink
{
    bool TryRecord(DebugProtocolTraceDirection direction, ReadOnlySpan<byte> validatedPayload);
}

public sealed record DebugProtocolTraceRecord(
    long Sequence,
    DateTimeOffset Timestamp,
    DebugProtocolTraceDirection Direction,
    ReadOnlyMemory<byte> RedactedPayload);

public sealed record DebugProtocolTraceHealth(long RetainedBytes, long DroppedRecords, long DroppedBytes);

/// <summary>Bounded disabled-unless-explicitly-supplied host trace buffer with structural redaction.</summary>
public sealed class DebugProtocolHostTraceBuffer : IDebugProtocolTraceSink
{
    private static readonly string[] SensitiveNames =
        ["token", "secret", "password", "authorization", "cookie", "environment", "env", "data",
         "variables", "expression", "value", "memoryReference", "output"];
    private readonly object _gate = new();
    private readonly Queue<DebugProtocolTraceRecord> _records = [];
    private readonly int _maximumRecords;
    private readonly int _maximumBytes;
    private long _sequence;
    private long _retainedBytes;
    private long _droppedRecords;
    private long _droppedBytes;

    public DebugProtocolHostTraceBuffer(int maximumRecords = 256, int maximumBytes = 1024 * 1024)
    {
        if (maximumRecords is <= 0 or > 4096 || maximumBytes is <= 0 or > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        _maximumRecords = maximumRecords;
        _maximumBytes = maximumBytes;
    }

    public bool TryRecord(DebugProtocolTraceDirection direction, ReadOnlySpan<byte> validatedPayload)
    {
        byte[] redacted;
        try { redacted = Redact(validatedPayload); }
        catch
        {
            lock (_gate) { _droppedRecords++; _droppedBytes += validatedPayload.Length; }
            return false;
        }
        lock (_gate)
        {
            if (redacted.Length > _maximumBytes)
            {
                _droppedRecords++;
                _droppedBytes += redacted.Length;
                return false;
            }
            while (_records.Count >= _maximumRecords || _retainedBytes + redacted.Length > _maximumBytes)
            {
                var removed = _records.Dequeue();
                _retainedBytes -= removed.RedactedPayload.Length;
                _droppedRecords++;
                _droppedBytes += removed.RedactedPayload.Length;
            }
            _records.Enqueue(new(checked(++_sequence), DateTimeOffset.UtcNow, direction, redacted));
            _retainedBytes += redacted.Length;
            return true;
        }
    }

    public IReadOnlyList<DebugProtocolTraceRecord> Snapshot()
    {
        lock (_gate) return _records.ToArray();
    }

    public DebugProtocolTraceHealth Health
    {
        get { lock (_gate) return new(_retainedBytes, _droppedRecords, _droppedBytes); }
    }

    public async ValueTask<ContentAddress> PersistHostDiagnosticAsync(
        IContentStore store,
        ContentScope scope,
        IReadOnlyDictionary<string, string> ownershipTags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ownershipTags);
        byte[] bytes;
        lock (_gate)
        {
            using var stream = new MemoryStream();
            foreach (var record in _records)
            {
                stream.Write(record.RedactedPayload.Span);
                stream.WriteByte((byte)'\n');
            }
            bytes = stream.ToArray();
        }
        await using var content = new MemoryStream(bytes, writable: false);
        var tags = new Dictionary<string, string>(ownershipTags, StringComparer.Ordinal)
        {
            ["kind"] = "host-diagnostic",
            ["artifact-kind"] = "debug-protocol-trace",
            ["model-visible"] = "false"
        };
        var info = await store.WriteAsync(scope, content, new ContentMetadata
        {
            ContentType = "application/x-ndjson",
            Name = "debug-protocol-trace.ndjson",
            Description = "Redacted host-only DAP protocol trace",
            Origin = ContentSource.System,
            Tags = tags
        }, new ContentWriteOptions { Mode = ContentWriteMode.Create }, cancellationToken).ConfigureAwait(false);
        return info.Address;
    }

    private static byte[] Redact(ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        Write(writer, document.RootElement, propertyName: null, depth: 0);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element, string? propertyName, int depth)
    {
        if (depth > 64) { writer.WriteStringValue("[REDACTED_DEPTH]"); return; }
        if (propertyName is not null && SensitiveNames.Any(name => propertyName.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            writer.WriteStringValue("[REDACTED]");
            return;
        }
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, property.Name, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Write(writer, item, propertyName, depth + 1);
                writer.WriteEndArray();
                break;
            default: element.WriteTo(writer); break;
        }
    }
}

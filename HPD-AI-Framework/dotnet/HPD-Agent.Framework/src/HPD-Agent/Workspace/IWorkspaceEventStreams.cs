using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// Low-level append log backend for workspace event streams. This layer owns
/// durable event ordering only; workspace spaces, access, roles, and paths live
/// above it.
/// </summary>
public interface IWorkspaceEventStreams
{
    Task<WorkspaceEventStreamAppendResult> AppendAsync(
        string streamId,
        AppendWorkspaceEventStreamRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WorkspaceEventRecord> ReadAsync(
        string streamId,
        WorkspaceEventStreamQuery query,
        CancellationToken cancellationToken = default);

    Task<WorkspaceEventStreamStat?> StatAsync(
        string streamId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string streamId,
        CancellationToken cancellationToken = default);
}

public sealed record AppendWorkspaceEventStreamRequest
{
    public required string SpaceId { get; init; }
    public required string Role { get; init; }
    public required ReadOnlyMemory<byte> Payload { get; init; }
    public long? ExpectedSequenceNumber { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceEventStreamAppendResult
{
    public long SequenceNumber { get; init; }
    public long NextSequenceNumber { get; init; }
}

public sealed record WorkspaceEventStreamStat
{
    public required string StreamId { get; init; }
    public long LatestSequenceNumber { get; init; }
    public long Count { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class InMemoryWorkspaceEventStreams : IWorkspaceEventStreams
{
    private readonly ConcurrentDictionary<string, List<WorkspaceEventRecord>> _streams = new();
    private readonly object _gate = new();

    public Task<WorkspaceEventStreamAppendResult> AppendAsync(
        string streamId,
        AppendWorkspaceEventStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var events = _streams.GetOrAdd(streamId, _ => []);
            var currentSequence = events.Count == 0 ? 0 : events[^1].SequenceNumber;
            EnsureExpectedSequence(streamId, request.ExpectedSequenceNumber, currentSequence);

            var nextSequence = currentSequence + 1;
            events.Add(new WorkspaceEventRecord
            {
                SpaceId = request.SpaceId,
                Role = request.Role,
                SequenceNumber = nextSequence,
                Payload = request.Payload.ToArray(),
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = request.Metadata
            });

            return Task.FromResult(new WorkspaceEventStreamAppendResult
            {
                SequenceNumber = nextSequence,
                NextSequenceNumber = nextSequence + 1
            });
        }
    }

    public async IAsyncEnumerable<WorkspaceEventRecord> ReadAsync(
        string streamId,
        WorkspaceEventStreamQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        List<WorkspaceEventRecord> snapshot;
        lock (_gate)
        {
            snapshot = _streams.TryGetValue(streamId, out var events)
                ? events.ToList()
                : [];
        }

        IEnumerable<WorkspaceEventRecord> results = snapshot
            .Where(evt => query.AfterSequenceNumber is null || evt.SequenceNumber > query.AfterSequenceNumber.Value)
            .OrderBy(evt => evt.SequenceNumber);

        if (query.Limit is not null)
            results = results.Take(query.Limit.Value);

        foreach (var evt in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return evt;
        }
    }

    public Task<WorkspaceEventStreamStat?> StatAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_streams.TryGetValue(streamId, out var events))
                return Task.FromResult<WorkspaceEventStreamStat?>(null);

            var last = events.LastOrDefault();
            return Task.FromResult<WorkspaceEventStreamStat?>(new WorkspaceEventStreamStat
            {
                StreamId = streamId,
                LatestSequenceNumber = last?.SequenceNumber ?? 0,
                Count = events.Count,
                UpdatedAt = last?.CreatedAt
            });
        }
    }

    public Task DeleteAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        cancellationToken.ThrowIfCancellationRequested();
        _streams.TryRemove(streamId, out _);
        return Task.CompletedTask;
    }

    internal static void EnsureExpectedSequence(string streamId, long? expected, long actual)
    {
        if (expected is not null && expected.Value != actual)
        {
            throw new WorkspaceConflictException(
                $"Event stream '{streamId}' sequence conflict.",
                expected.Value.ToString(),
                actual.ToString());
        }
    }
}

public sealed class FileWorkspaceEventStreams : IWorkspaceEventStreams
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _rootPath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public FileWorkspaceEventStreams(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        _rootPath = Path.Combine(basePath, "event-streams");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<WorkspaceEventStreamAppendResult> AppendAsync(
        string streamId,
        AppendWorkspaceEventStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var gate = _locks.GetOrAdd(streamId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = PathFor(streamId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var latest = await ReadLatestSequenceAsync(path, cancellationToken).ConfigureAwait(false);
            InMemoryWorkspaceEventStreams.EnsureExpectedSequence(streamId, request.ExpectedSequenceNumber, latest);

            var next = latest + 1;
            var line = JsonSerializer.Serialize(new FileEventRecord
            {
                SpaceId = request.SpaceId,
                Role = request.Role,
                SequenceNumber = next,
                PayloadBase64 = Convert.ToBase64String(request.Payload.Span),
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = request.Metadata
            }, JsonOptions);

            await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return new WorkspaceEventStreamAppendResult
            {
                SequenceNumber = next,
                NextSequenceNumber = next + 1
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async IAsyncEnumerable<WorkspaceEventRecord> ReadAsync(
        string streamId,
        WorkspaceEventStreamQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var path = PathFor(streamId);
        if (!File.Exists(path))
            yield break;

        var yielded = 0;
        await foreach (var record in ReadFileRecordsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (query.AfterSequenceNumber is not null && record.SequenceNumber <= query.AfterSequenceNumber.Value)
                continue;
            if (query.Limit is not null && yielded >= query.Limit.Value)
                yield break;

            yielded++;
            yield return Map(record);
        }
    }

    public async Task<WorkspaceEventStreamStat?> StatAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        cancellationToken.ThrowIfCancellationRequested();

        var path = PathFor(streamId);
        if (!File.Exists(path))
            return null;

        long count = 0;
        FileEventRecord? last = null;
        await foreach (var record in ReadFileRecordsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            count++;
            last = record;
        }

        return new WorkspaceEventStreamStat
        {
            StreamId = streamId,
            LatestSequenceNumber = last?.SequenceNumber ?? 0,
            Count = count,
            UpdatedAt = last?.CreatedAt
        };
    }

    public Task DeleteAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        cancellationToken.ThrowIfCancellationRequested();

        var path = PathFor(streamId);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private static async Task<long> ReadLatestSequenceAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return 0;

        long latest = 0;
        await foreach (var record in ReadFileRecordsAsync(path, cancellationToken).ConfigureAwait(false))
            latest = record.SequenceNumber;

        return latest;
    }

    private static async IAsyncEnumerable<FileEventRecord> ReadFileRecordsAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var record = JsonSerializer.Deserialize<FileEventRecord>(line, JsonOptions);
            if (record is not null)
                yield return record;
        }
    }

    private string PathFor(string streamId)
    {
        var bytes = Encoding.UTF8.GetBytes(streamId);
        var name = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return Path.Combine(_rootPath, name + ".jsonl");
    }

    private static WorkspaceEventRecord Map(FileEventRecord record) => new()
    {
        SpaceId = record.SpaceId,
        Role = record.Role,
        SequenceNumber = record.SequenceNumber,
        Payload = Convert.FromBase64String(record.PayloadBase64),
        CreatedAt = record.CreatedAt,
        Metadata = record.Metadata
    };

    private sealed record FileEventRecord
    {
        public required string SpaceId { get; init; }
        public required string Role { get; init; }
        public long SequenceNumber { get; init; }
        public required string PayloadBase64 { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    }
}

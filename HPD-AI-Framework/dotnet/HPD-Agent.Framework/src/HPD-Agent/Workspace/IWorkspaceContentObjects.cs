using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// Low-level workspace payload backend. This layer owns raw content bytes and
/// versions only; workspace spaces, access, roles, and paths live above it.
/// </summary>
public interface IWorkspaceContentObjects
{
    Task<WorkspaceContentObjectWriteResult> WriteAsync(
        string contentId,
        string version,
        Stream data,
        WorkspaceContentObjectWriteRequest request,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default);

    Task<WorkspaceContentObjectStat?> StatAsync(
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default);

    Task<Uri?> CreateReadUriAsync(
        string contentId,
        string? version,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string contentId,
        string? ifMatchVersion = null,
        CancellationToken cancellationToken = default);

    Task DeleteVersionAsync(
        string contentId,
        string version,
        CancellationToken cancellationToken = default);
}

public sealed record WorkspaceContentObjectWriteRequest
{
    public string ContentType { get; init; } = "application/octet-stream";
    public string? Name { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceContentObjectWriteResult
{
    public required string ContentId { get; init; }
    public required string Version { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required string Checksum { get; init; }
    public required string StorageKey { get; init; }
    public string? Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceContentObjectStat
{
    public required string ContentId { get; init; }
    public required string Version { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required string Checksum { get; init; }
    public required string StorageKey { get; init; }
    public string? Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Non-production content object backend for tests and in-memory runtimes.
/// </summary>
public sealed class InMemoryWorkspaceContentObjects : IWorkspaceContentObjects
{
    private readonly ConcurrentDictionary<string, ContentObjectRecord> _content = new();
    private readonly object _gate = new();

    public async Task<WorkspaceContentObjectWriteResult> WriteAsync(
        string contentId,
        string version,
        Stream data,
        WorkspaceContentObjectWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = await ReadAllBytesAsync(data, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var updatedVersion = ContentVersionRecord.Create(contentId, version, bytes, request);
            if (!_content.TryGetValue(contentId, out var existing))
            {
                var created = new ContentObjectRecord(contentId, updatedVersion, []);
                _content[created.Id] = created;
                return Map(created.Id, created.Current);
            }

            var updated = existing with
            {
                Current = updatedVersion,
                PreviousVersions = [.. existing.PreviousVersions, existing.Current]
            };
            _content[updated.Id] = updated;
            return Map(updated.Id, updated.Current);
        }
    }

    public Task<Stream?> OpenReadAsync(
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_content.TryGetValue(contentId, out var record))
            return Task.FromResult<Stream?>(null);

        var contentVersion = record.FindVersion(version);
        Stream? stream = contentVersion is null
            ? null
            : new MemoryStream(contentVersion.Bytes, writable: false);
        return Task.FromResult(stream);
    }

    public Task<WorkspaceContentObjectStat?> StatAsync(
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_content.TryGetValue(contentId, out var record))
            return Task.FromResult<WorkspaceContentObjectStat?>(null);

        var contentVersion = record.FindVersion(version);
        return Task.FromResult(contentVersion is null ? null : MapStat(contentId, contentVersion));
    }

    public Task<Uri?> CreateReadUriAsync(
        string contentId,
        string? version,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Uri?>(null);

    public Task DeleteAsync(
        string contentId,
        string? ifMatchVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_content.TryGetValue(contentId, out var existing))
                return Task.CompletedTask;
            EnsureVersionMatches(existing.Current.Version, ifMatchVersion, $"Content '{contentId}' version conflict.");
            _content.TryRemove(contentId, out _);
            return Task.CompletedTask;
        }
    }

    public Task DeleteVersionAsync(
        string contentId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_content.TryGetValue(contentId, out var existing))
                return Task.CompletedTask;

            if (existing.Current.Version == version)
            {
                var previous = existing.PreviousVersions.ToList();
                if (previous.Count == 0)
                {
                    _content.TryRemove(contentId, out _);
                    return Task.CompletedTask;
                }

                var newCurrent = previous[^1];
                previous.RemoveAt(previous.Count - 1);
                _content[contentId] = existing with
                {
                    Current = newCurrent,
                    PreviousVersions = previous
                };
                return Task.CompletedTask;
            }

            _content[contentId] = existing with
            {
                PreviousVersions = existing.PreviousVersions
                    .Where(candidate => candidate.Version != version)
                    .ToList()
            };
            return Task.CompletedTask;
        }
    }

    private static WorkspaceContentObjectWriteResult Map(string contentId, ContentVersionRecord record) => new()
    {
        ContentId = contentId,
        Version = record.Version,
        ContentType = record.ContentType,
        SizeBytes = record.SizeBytes,
        Checksum = record.Checksum,
        StorageKey = record.StorageKey,
        Name = record.Name,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        Metadata = record.Metadata
    };

    private static WorkspaceContentObjectStat MapStat(string contentId, ContentVersionRecord record) => new()
    {
        ContentId = contentId,
        Version = record.Version,
        ContentType = record.ContentType,
        SizeBytes = record.SizeBytes,
        Checksum = record.Checksum,
        StorageKey = record.StorageKey,
        Name = record.Name,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        Metadata = record.Metadata
    };

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static void EnsureVersionMatches(string actual, string? expected, string message)
    {
        if (expected is not null && actual != expected)
            throw new WorkspaceConflictException(message, expected, actual);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ContentObjectRecord(
        string Id,
        ContentVersionRecord Current,
        IReadOnlyList<ContentVersionRecord> PreviousVersions)
    {
        public ContentVersionRecord? FindVersion(string? version)
        {
            if (version is null || Current.Version == version)
                return Current;

            return PreviousVersions.FirstOrDefault(candidate => candidate.Version == version);
        }
    }

    private sealed record ContentVersionRecord(
        string Version,
        byte[] Bytes,
        string ContentType,
        long SizeBytes,
        string Checksum,
        string StorageKey,
        string? Name,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyDictionary<string, string>? Metadata)
    {
        public static ContentVersionRecord Create(
            string contentId,
            string version,
            byte[] bytes,
            WorkspaceContentObjectWriteRequest request)
        {
            var now = DateTimeOffset.UtcNow;
            return new ContentVersionRecord(
                Version: version,
                Bytes: bytes,
                ContentType: string.IsNullOrWhiteSpace(request.ContentType)
                    ? "application/octet-stream"
                    : request.ContentType,
                SizeBytes: bytes.LongLength,
                Checksum: Hash(bytes),
                StorageKey: $"memory://{contentId}/{version}",
                Name: request.Name,
                CreatedAt: now,
                UpdatedAt: now,
                Metadata: request.Metadata);
        }
    }
}

/// <summary>
/// Local file content object backend. Payload bytes are streamed to files under
/// payloads/{contentId}/{version}.bin and small sidecar metadata files.
/// </summary>
public sealed class FileWorkspaceContentObjects : IWorkspaceContentObjects
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _payloadRoot;
    private readonly object _gate = new();

    public FileWorkspaceContentObjects(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        _payloadRoot = Path.Combine(basePath, "payloads");
        Directory.CreateDirectory(_payloadRoot);
    }

    public async Task<WorkspaceContentObjectWriteResult> WriteAsync(
        string contentId,
        string version,
        Stream data,
        WorkspaceContentObjectWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var existing = ReadStat(contentId, version);
            if (existing is not null)
                throw new WorkspaceConflictException(
                    $"Content '{contentId}' version '{version}' already exists.",
                    version,
                    existing.Version);
        }

        var contentDirectory = ContentDirectory(contentId);
        Directory.CreateDirectory(contentDirectory);
        var payloadPath = PayloadPath(contentId, version);

        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var output = new FileStream(
            payloadPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            useAsync: true))
        {
            var buffer = new byte[1024 * 128];
            int read;
            while ((read = await data.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer.AsSpan(0, read));
                size += read;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        var stat = new WorkspaceContentObjectStat
        {
            ContentId = contentId,
            Version = version,
            ContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType,
            SizeBytes = size,
            Checksum = checksum,
            StorageKey = StorageKey(contentId, version),
            Name = request.Name,
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = request.Metadata
        };

        lock (_gate)
        {
            WriteStat(stat);
            File.WriteAllText(CurrentVersionPath(contentId), version);
        }

        return new WorkspaceContentObjectWriteResult
        {
            ContentId = stat.ContentId,
            Version = stat.Version,
            ContentType = stat.ContentType,
            SizeBytes = stat.SizeBytes,
            Checksum = stat.Checksum,
            StorageKey = stat.StorageKey,
            Name = stat.Name,
            CreatedAt = stat.CreatedAt,
            UpdatedAt = stat.UpdatedAt,
            Metadata = stat.Metadata
        };
    }

    public Task<Stream?> OpenReadAsync(
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedVersion = version ?? ReadCurrentVersion(contentId)?.Version;
        if (resolvedVersion is null)
            return Task.FromResult<Stream?>(null);

        var path = PayloadPath(contentId, resolvedVersion);
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true));
    }

    public Task<WorkspaceContentObjectStat?> StatAsync(
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        var stat = version is null ? ReadCurrentVersion(contentId) : ReadStat(contentId, version);
        return Task.FromResult(stat);
    }

    public Task<Uri?> CreateReadUriAsync(
        string contentId,
        string? version,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedVersion = version ?? ReadCurrentVersion(contentId)?.Version;
        if (resolvedVersion is null)
            return Task.FromResult<Uri?>(null);

        var path = PayloadPath(contentId, resolvedVersion);
        return Task.FromResult(File.Exists(path) ? new Uri(path) : null);
    }

    public Task DeleteAsync(
        string contentId,
        string? ifMatchVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var current = ReadCurrentVersion(contentId);
            if (current is null)
                return Task.CompletedTask;
            EnsureVersionMatches(current.Version, ifMatchVersion, $"Content '{contentId}' version conflict.");

            var directory = ContentDirectory(contentId);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            return Task.CompletedTask;
        }
    }

    public Task DeleteVersionAsync(
        string contentId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var payloadPath = PayloadPath(contentId, version);
            if (File.Exists(payloadPath))
                File.Delete(payloadPath);

            var statPath = StatPath(contentId, version);
            if (File.Exists(statPath))
                File.Delete(statPath);

            var currentPath = CurrentVersionPath(contentId);
            if (File.Exists(currentPath) && File.ReadAllText(currentPath).Trim() == version)
                File.Delete(currentPath);

            var directory = ContentDirectory(contentId);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);

            return Task.CompletedTask;
        }
    }

    private WorkspaceContentObjectStat? ReadCurrentVersion(string contentId)
    {
        var path = CurrentVersionPath(contentId);
        if (!File.Exists(path))
            return null;

        var version = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(version) ? null : ReadStat(contentId, version);
    }

    private static void EnsureVersionMatches(string actual, string? expected, string message)
    {
        if (expected is not null && actual != expected)
            throw new WorkspaceConflictException(message, expected, actual);
    }

    private void WriteStat(WorkspaceContentObjectStat stat)
    {
        var path = StatPath(stat.ContentId, stat.Version);
        File.WriteAllText(path, JsonSerializer.Serialize(stat, JsonOptions));
    }

    private WorkspaceContentObjectStat? ReadStat(string contentId, string version)
    {
        var path = StatPath(contentId, version);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<WorkspaceContentObjectStat>(
            File.ReadAllText(path),
            JsonOptions);
    }

    private string ContentDirectory(string contentId) => Path.Combine(_payloadRoot, SafeSegment(contentId));
    private string PayloadPath(string contentId, string version) => Path.Combine(ContentDirectory(contentId), $"{SafeSegment(version)}.bin");
    private string StatPath(string contentId, string version) => Path.Combine(ContentDirectory(contentId), $"{SafeSegment(version)}.json");
    private string CurrentVersionPath(string contentId) => Path.Combine(ContentDirectory(contentId), "current.txt");
    private static string StorageKey(string contentId, string version) => $"payloads/{SafeSegment(contentId)}/{SafeSegment(version)}.bin";
    private static string SafeSegment(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_'));
}

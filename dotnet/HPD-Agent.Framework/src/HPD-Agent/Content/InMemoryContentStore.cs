using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace HPD.Agent;

/// <summary>
/// In-memory implementation of IContentStore (for testing and development).
/// Supports metadata-based organization, explicit versioned writes, and full ContentQuery filtering.
/// </summary>
/// <remarks>
/// <para><b>Use Cases:</b></para>
/// <list type="bullet">
/// <item>Unit tests that need content storage</item>
/// <item>Development/prototyping without file system dependencies</item>
/// <item>Ephemeral sessions that don't need persistence</item>
/// </list>
/// <para><b>Limitations:</b></para>
/// <list type="bullet">
/// <item>Content lost on process restart (no persistence)</item>
/// <item>Memory usage grows unbounded (no automatic cleanup)</item>
/// <item>Not suitable for production with large data sets</item>
/// </list>
/// </remarks>
public class InMemoryContentStore : IContentStore
{
    // Storage structure: scope -> contentId -> StoredContent
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StoredContent>> _scopedContent = new();
    // Name index: scope -> name -> latest contentId
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _nameIndex = new();
    private readonly object _writeLock = new();

    private record StoredContent(
        string Id,
        string Version,
        byte[] Data,
        string ContentType,
        DateTime CreatedAt,
        DateTime? LastModified,
        ContentMetadata? Metadata,
        string? ContentHash);

    // ═══════════════════════════════════════════════════════════════════
    // IContentStore Implementation
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<ContentInfo> WriteAsync(
        string? scope,
        Stream data,
        ContentMetadata metadata,
        ContentWriteOptions options,
        CancellationToken cancellationToken = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (metadata == null) throw new ArgumentNullException(nameof(metadata));
        if (options == null) throw new ArgumentNullException(nameof(options));
        var contentType = string.IsNullOrWhiteSpace(metadata.ContentType)
            ? "application/octet-stream"
            : metadata.ContentType;
        var bytes = await ReadAllBytesAsync(data, cancellationToken).ConfigureAwait(false);

        var actualScope = scope ?? "global";
        lock (_writeLock)
        {
            var scopeDict = _scopedContent.GetOrAdd(actualScope, _ => new ConcurrentDictionary<string, StoredContent>());
            var nameIndex = _nameIndex.GetOrAdd(actualScope, _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            return options.Mode switch
            {
                ContentWriteMode.Create or ContentWriteMode.Stage => Create(scopeDict, nameIndex, bytes, contentType, metadata, options),
                ContentWriteMode.ReplaceById => ReplaceById(scopeDict, nameIndex, bytes, contentType, metadata, options),
                ContentWriteMode.ReplaceByName => ReplaceByName(scopeDict, nameIndex, bytes, contentType, metadata, options),
                ContentWriteMode.Append => Append(scopeDict, nameIndex, bytes, contentType, metadata, options),
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unsupported content write mode.")
            };
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return Task.FromResult<Stream?>(null);

        var actualScope = scope ?? "global";

        if (!_scopedContent.TryGetValue(actualScope, out var scopeDict))
            return Task.FromResult<Stream?>(null);

        if (!scopeDict.TryGetValue(contentId, out var item))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(new MemoryStream(item.Data, writable: false));
    }

    /// <inheritdoc />
    public Task<Uri?> CreateReadUriAsync(
        string? scope,
        string contentId,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Uri?>(null);
    }

    /// <inheritdoc />
    public Task<ContentInfo?> StatAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return Task.FromResult<ContentInfo?>(null);

        var actualScope = scope ?? "global";
        if (!_scopedContent.TryGetValue(actualScope, out var scopeDict))
            return Task.FromResult<ContentInfo?>(null);

        return scopeDict.TryGetValue(contentId, out var item)
            ? Task.FromResult<ContentInfo?>(MapToContentInfo(item))
            : Task.FromResult<ContentInfo?>(null);
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        string? scope,
        string contentId,
        ContentDeleteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return Task.CompletedTask;

        var actualScope = scope ?? "global";

        if (_scopedContent.TryGetValue(actualScope, out var scopeDict) &&
            scopeDict.TryGetValue(contentId, out var existing))
        {
            if (options?.IfMatchVersion != null && existing.Version != options.IfMatchVersion)
                throw new ContentConflictException(
                    $"Content '{contentId}' version conflict.",
                    contentId,
                    options.IfMatchVersion,
                    existing.Version);
        }

        if (_scopedContent.TryGetValue(actualScope, out scopeDict) &&
            scopeDict.TryRemove(contentId, out var removed))
        {
            // Also remove from name index
            var name = removed.Metadata?.Name;
            if (name != null && _nameIndex.TryGetValue(actualScope, out var nameIndex))
                nameIndex.TryRemove(MakeNameKey(removed.Metadata), out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ContentInfo>> QueryAsync(
        string? scope = null,
        ContentQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<StoredContent> allContent;

        if (scope == null)
        {
            allContent = _scopedContent.Values.SelectMany(d => d.Values);
        }
        else
        {
            if (!_scopedContent.TryGetValue(scope, out var scopeDict))
                return Task.FromResult<IReadOnlyList<ContentInfo>>(Array.Empty<ContentInfo>());
            allContent = scopeDict.Values;
        }

        // Apply filters
        if (query?.ContentType != null)
            allContent = allContent.Where(a => a.ContentType.Equals(query.ContentType, StringComparison.OrdinalIgnoreCase));

        if (query?.CreatedAfter != null)
            allContent = allContent.Where(a => a.CreatedAt >= query.CreatedAfter.Value);

        if (query?.Tags != null)
        {
            allContent = allContent.Where(a =>
                a.Metadata?.Tags != null &&
                query.Tags.All(kv =>
                    a.Metadata.Tags.TryGetValue(kv.Key, out var v) && v == kv.Value));
        }

        if (query?.Name != null)
            allContent = allContent.Where(a =>
                (a.Metadata?.Name ?? a.Id).Equals(query.Name, StringComparison.OrdinalIgnoreCase));

        var results = allContent.Select(MapToContentInfo);

        if (query?.Limit != null)
            results = results.Take(query.Limit.Value);

        return Task.FromResult<IReadOnlyList<ContentInfo>>(results.ToList());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Testing Helper Methods
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Clear all content in all scopes (for testing).</summary>
    public void Clear()
    {
        _scopedContent.Clear();
        _nameIndex.Clear();
    }

    /// <summary>Total content count across all scopes (for testing).</summary>
    public int Count => _scopedContent.Values.Sum(d => d.Count);

    /// <summary>Content count within a specific scope (for testing).</summary>
    public int CountInScope(string scope) =>
        _scopedContent.TryGetValue(scope, out var d) ? d.Count : 0;

    /// <summary>Check if content exists in a specific scope (for testing).</summary>
    public bool Contains(string scope, string contentId) =>
        _scopedContent.TryGetValue(scope, out var d) && d.ContainsKey(contentId);

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static ContentInfo Create(
        ConcurrentDictionary<string, StoredContent> scopeDict,
        ConcurrentDictionary<string, string> nameIndex,
        byte[] bytes,
        string contentType,
        ContentMetadata metadata,
        ContentWriteOptions options)
    {
        var name = metadata.Name;
        var nameKey = MakeNameKey(metadata);
        if (options.FailIfNameExists &&
            name != null &&
            nameIndex.ContainsKey(nameKey))
        {
            throw new ContentConflictException($"Content named '{name}' already exists.");
        }

        var content = new StoredContent(
            Id: Guid.NewGuid().ToString("N"),
            Version: NewVersion(),
            Data: bytes,
            ContentType: contentType,
            CreatedAt: DateTime.UtcNow,
            LastModified: null,
            Metadata: metadata,
            ContentHash: ComputeHash(bytes));

        scopeDict[content.Id] = content;
        if (name != null)
            nameIndex[nameKey] = content.Id;

        return MapToContentInfo(content);
    }

    private static ContentInfo ReplaceById(
        ConcurrentDictionary<string, StoredContent> scopeDict,
        ConcurrentDictionary<string, string> nameIndex,
        byte[] bytes,
        string contentType,
        ContentMetadata metadata,
        ContentWriteOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ContentId))
            throw new ArgumentException("ReplaceById requires ContentWriteOptions.ContentId.", nameof(options));

        if (!scopeDict.TryGetValue(options.ContentId, out var existing))
            throw new FileNotFoundException($"Content '{options.ContentId}' was not found.");

        EnsureVersionMatches(existing, options.IfMatchVersion);

        var updated = existing with
        {
            Version = NewVersion(),
            Data = bytes,
            ContentType = contentType,
            LastModified = DateTime.UtcNow,
            Metadata = metadata,
            ContentHash = ComputeHash(bytes)
        };
        scopeDict[updated.Id] = updated;
        if (existing.Metadata?.Name != null)
            nameIndex.TryRemove(MakeNameKey(existing.Metadata), out _);
        if (metadata.Name != null)
            nameIndex[MakeNameKey(metadata)] = updated.Id;
        return MapToContentInfo(updated);
    }

    private static ContentInfo ReplaceByName(
        ConcurrentDictionary<string, StoredContent> scopeDict,
        ConcurrentDictionary<string, string> nameIndex,
        byte[] bytes,
        string contentType,
        ContentMetadata metadata,
        ContentWriteOptions options)
    {
        if (string.IsNullOrWhiteSpace(metadata.Name))
            throw new ArgumentException("ReplaceByName requires ContentMetadata.Name.", nameof(metadata));

        if (!nameIndex.TryGetValue(MakeNameKey(metadata), out var contentId) ||
            !scopeDict.ContainsKey(contentId))
        {
            throw new FileNotFoundException($"Content named '{metadata.Name}' was not found.");
        }

        return ReplaceById(scopeDict, nameIndex, bytes, contentType, metadata, options with { ContentId = contentId });
    }

    private static ContentInfo Append(
        ConcurrentDictionary<string, StoredContent> scopeDict,
        ConcurrentDictionary<string, string> nameIndex,
        byte[] bytes,
        string contentType,
        ContentMetadata metadata,
        ContentWriteOptions options)
    {
        string? contentId = options.ContentId;
        if (contentId == null && metadata.Name != null)
            nameIndex.TryGetValue(MakeNameKey(metadata), out contentId);

        if (contentId == null || !scopeDict.TryGetValue(contentId, out var existing))
            return Create(scopeDict, nameIndex, bytes, contentType, metadata, options with { Mode = ContentWriteMode.Create });

        EnsureVersionMatches(existing, options.IfMatchVersion);

        var appended = new byte[existing.Data.Length + bytes.Length];
        Buffer.BlockCopy(existing.Data, 0, appended, 0, existing.Data.Length);
        Buffer.BlockCopy(bytes, 0, appended, existing.Data.Length, bytes.Length);

        var updated = existing with
        {
            Version = NewVersion(),
            Data = appended,
            ContentType = contentType,
            LastModified = DateTime.UtcNow,
            Metadata = metadata,
            ContentHash = ComputeHash(appended)
        };
        scopeDict[updated.Id] = updated;
        return MapToContentInfo(updated);
    }

    private static void EnsureVersionMatches(StoredContent existing, string? expectedVersion)
    {
        if (expectedVersion != null && existing.Version != expectedVersion)
        {
            throw new ContentConflictException(
                $"Content '{existing.Id}' version conflict.",
                existing.Id,
                expectedVersion,
                existing.Version);
        }
    }

    private static ContentInfo MapToContentInfo(StoredContent item)
    {
        var extendedMeta = item.ContentHash != null
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["contentHash"] = item.ContentHash }
            : null;

        return new ContentInfo
        {
            Id = item.Id,
            Version = item.Version,
            Name = item.Metadata?.Name ?? item.Id,
            ContentType = item.ContentType,
            SizeBytes = item.Data.Length,
            CreatedAt = item.CreatedAt,
            LastModified = item.LastModified,
            LastAccessed = null,
            Origin = item.Metadata?.Origin ?? ContentSource.User,
            Description = item.Metadata?.Description,
            Tags = item.Metadata?.Tags,
            OriginalSource = item.Metadata?.OriginalSource,
            ExtendedMetadata = extendedMeta
        };
    }

    private static string ComputeHash(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string NewVersion() => $"rev:{Guid.NewGuid():N}";

    private static string MakeNameKey(ContentMetadata metadata)
    {
        var kind = metadata.Tags != null && metadata.Tags.TryGetValue("kind", out var value)
            ? value.Trim().Trim('/')
            : "";
        return $"{kind}/{metadata.Name}".ToLowerInvariant();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var buffer))
        {
            var result = new byte[buffer.Count];
            Buffer.BlockCopy(buffer.Array!, buffer.Offset, result, 0, buffer.Count);
            return result;
        }

        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        return copy.ToArray();
    }
}

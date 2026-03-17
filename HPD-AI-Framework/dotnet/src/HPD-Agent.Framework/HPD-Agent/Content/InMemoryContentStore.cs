using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace HPD.Agent;

/// <summary>
/// In-memory implementation of IContentStore (for testing and development).
/// Supports folder-based organization via tags, named upsert semantics, and full ContentQuery filtering.
/// </summary>
/// <remarks>
/// <para><b>Use Cases:</b></para>
/// <list type="bullet">
/// <item>Unit tests that need content storage</item>
/// <item>Development/prototyping without file system dependencies</item>
/// <item>Ephemeral sessions that don't need persistence</item>
/// </list>
/// <para><b>Named Upsert Semantics:</b></para>
/// <para>
/// When ContentMetadata.Name is provided, PutAsync behaves as an upsert keyed on (scope, Name):
/// - Same name + same content hash → no-op, returns existing ID
/// - Same name + different content → overwrites in place, returns same ID
/// - No name → always inserts as a new entry with a generated ID
/// </para>
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
    // Name index: scope -> name -> contentId  (for named upsert lookup)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _nameIndex = new();
    private readonly object _writeLock = new();

    private record StoredContent(
        string Id,
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
    public Task<string> PutAsync(
        string? scope,
        byte[] data,
        string contentType,
        ContentMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(contentType))
            contentType = "application/octet-stream";

        var actualScope = scope ?? "global";
        var name = metadata?.Name;

        // Named upsert: if Name provided, check for existing entry
        if (name != null)
        {
            lock (_writeLock)
            {
                var nameIndex = _nameIndex.GetOrAdd(actualScope, _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                var scopeDict = _scopedContent.GetOrAdd(actualScope, _ => new ConcurrentDictionary<string, StoredContent>());

                if (nameIndex.TryGetValue(name, out var existingId) &&
                    scopeDict.TryGetValue(existingId, out var existing))
                {
                    var newHash = ComputeHash(data);
                    if (existing.ContentHash == newHash)
                    {
                        // Same content bytes — update metadata if it changed (e.g. new tags from LinkSkillDocumentAsync)
                        if (metadata != null && !ReferenceEquals(existing.Metadata, metadata))
                        {
                            var refreshed = existing with { Metadata = metadata };
                            scopeDict[existingId] = refreshed;
                        }
                        return Task.FromResult(existingId);
                    }
                    else
                    {
                        // Different content — overwrite in place
                        var updated = existing with
                        {
                            Data = data,
                            ContentType = contentType,
                            LastModified = DateTime.UtcNow,
                            Metadata = metadata,
                            ContentHash = newHash
                        };
                        scopeDict[existingId] = updated;
                        return Task.FromResult(existingId);
                    }
                }

                // New named entry
                var newId = Guid.NewGuid().ToString("N");
                var hash = ComputeHash(data);
                var content = new StoredContent(
                    Id: newId,
                    Data: data,
                    ContentType: contentType,
                    CreatedAt: DateTime.UtcNow,
                    LastModified: null,
                    Metadata: metadata,
                    ContentHash: hash);
                scopeDict[newId] = content;
                nameIndex[name] = newId;
                return Task.FromResult(newId);
            }
        }

        // Unnamed insert — always creates a new entry
        var id = Guid.NewGuid().ToString("N");
        var scopeStorage = _scopedContent.GetOrAdd(actualScope, _ => new ConcurrentDictionary<string, StoredContent>());
        var item = new StoredContent(
            Id: id,
            Data: data,
            ContentType: contentType,
            CreatedAt: DateTime.UtcNow,
            LastModified: null,
            Metadata: metadata,
            ContentHash: null); // Unnamed — no hash tracking needed
        scopeStorage[id] = item;
        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task<ContentData?> GetAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return Task.FromResult<ContentData?>(null);

        var actualScope = scope ?? "global";

        if (!_scopedContent.TryGetValue(actualScope, out var scopeDict))
            return Task.FromResult<ContentData?>(null);

        if (!scopeDict.TryGetValue(contentId, out var item))
            return Task.FromResult<ContentData?>(null);

        return Task.FromResult<ContentData?>(MapToContentData(item));
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return Task.CompletedTask;

        var actualScope = scope ?? "global";

        if (_scopedContent.TryGetValue(actualScope, out var scopeDict) &&
            scopeDict.TryRemove(contentId, out var removed))
        {
            // Also remove from name index
            var name = removed.Metadata?.Name;
            if (name != null && _nameIndex.TryGetValue(actualScope, out var nameIndex))
                nameIndex.TryRemove(name, out _);
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

    private static ContentData MapToContentData(StoredContent item) => new()
    {
        Id = item.Id,
        Data = item.Data,
        ContentType = item.ContentType,
        Info = MapToContentInfo(item)
    };

    private static ContentInfo MapToContentInfo(StoredContent item)
    {
        var extendedMeta = item.ContentHash != null
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["contentHash"] = item.ContentHash }
            : null;

        return new ContentInfo
        {
            Id = item.Id,
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
}

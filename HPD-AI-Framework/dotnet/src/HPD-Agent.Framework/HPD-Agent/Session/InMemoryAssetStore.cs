using System.Collections.Concurrent;

namespace HPD.Agent;

/// <summary>
/// In-memory storage for binary assets (for testing and development).
/// </summary>
/// <remarks>
/// <para><b>Use Cases:</b></para>
/// <list type="bullet">
/// <item>Unit tests that need asset storage</item>
/// <item>Development/prototyping without file system dependencies</item>
/// <item>Ephemeral sessions that don't need persistence</item>
/// </list>
/// <para><b>Limitations:</b></para>
/// <list type="bullet">
/// <item>Assets lost on process restart (no persistence)</item>
/// <item>Memory usage grows unbounded (no automatic cleanup)</item>
/// <item>Not suitable for production (use LocalFileAssetStore or cloud storage)</item>
/// </list>
/// </remarks>
public class InMemoryAssetStore : IAssetStore
{
    // Storage structure: scope -> contentId -> StoredAsset
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StoredAsset>> _scopedAssets = new();

    /// <summary>
    /// Internal asset storage structure.
    /// </summary>
    private record StoredAsset(
        string Id,
        byte[] Data,
        string ContentType,
        DateTime CreatedAt,
        ContentMetadata? Metadata);

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

        // Use "global" as default scope if null
        var actualScope = scope ?? "global";

        // Generate unique asset ID
        var assetId = Guid.NewGuid().ToString("N");

        // Get or create scope dictionary
        var scopeDict = _scopedAssets.GetOrAdd(actualScope, _ => new ConcurrentDictionary<string, StoredAsset>());

        // Store in memory
        var asset = new StoredAsset(
            Id: assetId,
            Data: data,
            ContentType: contentType,
            CreatedAt: DateTime.UtcNow,
            Metadata: metadata);

        scopeDict[assetId] = asset;

        return Task.FromResult(assetId);
    }

    /// <inheritdoc />
    public Task<ContentData?> GetAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return Task.FromResult<ContentData?>(null);

        // Use "global" as default scope if null
        var actualScope = scope ?? "global";

        // Try to get from scope
        if (!_scopedAssets.TryGetValue(actualScope, out var scopeDict))
            return Task.FromResult<ContentData?>(null);

        if (!scopeDict.TryGetValue(contentId, out var asset))
            return Task.FromResult<ContentData?>(null);

        // Map StoredAsset → ContentData
        var contentData = new ContentData
        {
            Id = asset.Id,
            Data = asset.Data,
            ContentType = asset.ContentType,
            Info = new ContentInfo
            {
                Id = asset.Id,
                Name = asset.Metadata?.Name ?? asset.Id,
                ContentType = asset.ContentType,
                SizeBytes = asset.Data.Length,
                CreatedAt = asset.CreatedAt,
                Origin = asset.Metadata?.Origin ?? ContentSource.User,
                Description = asset.Metadata?.Description,
                LastModified = null,
                LastAccessed = null,
                Tags = asset.Metadata?.Tags,
                OriginalSource = asset.Metadata?.OriginalSource,
                ExtendedMetadata = null
            }
        };

        return Task.FromResult<ContentData?>(contentData);
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return Task.CompletedTask;

        // Use "global" as default scope if null
        var actualScope = scope ?? "global";

        // Try to get scope dictionary and remove asset
        if (_scopedAssets.TryGetValue(actualScope, out var scopeDict))
        {
            scopeDict.TryRemove(contentId, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ContentInfo>> QueryAsync(
        string? scope = null,
        ContentQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<StoredAsset> allAssets;

        if (scope == null)
        {
            // Query across ALL scopes
            allAssets = _scopedAssets.Values
                .SelectMany(scopeDict => scopeDict.Values);
        }
        else
        {
            // Query within specific scope
            if (!_scopedAssets.TryGetValue(scope, out var scopeDict))
            {
                return Task.FromResult<IReadOnlyList<ContentInfo>>(Array.Empty<ContentInfo>());
            }
            allAssets = scopeDict.Values;
        }

        // Apply filters (ContentType, CreatedAfter, Limit)
        if (query?.ContentType != null)
        {
            allAssets = allAssets.Where(a =>
                a.ContentType.Equals(query.ContentType, StringComparison.OrdinalIgnoreCase));
        }

        if (query?.CreatedAfter != null)
        {
            allAssets = allAssets.Where(a => a.CreatedAt >= query.CreatedAfter.Value);
        }

        // Convert to ContentInfo
        var results = allAssets.Select(a => new ContentInfo
        {
            Id = a.Id,
            Name = a.Metadata?.Name ?? a.Id,
            ContentType = a.ContentType,
            SizeBytes = a.Data.Length,
            CreatedAt = a.CreatedAt,
            Origin = a.Metadata?.Origin ?? ContentSource.User,
            Description = a.Metadata?.Description,
            LastModified = null,
            LastAccessed = null,
            Tags = a.Metadata?.Tags,
            OriginalSource = a.Metadata?.OriginalSource,
            ExtendedMetadata = null
        });

        // Apply limit
        if (query?.Limit != null)
        {
            results = results.Take(query.Limit.Value);
        }

        return Task.FromResult<IReadOnlyList<ContentInfo>>(results.ToList());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Testing Helper Methods
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clear all assets in all scopes (for testing).
    /// </summary>
    public void Clear()
    {
        _scopedAssets.Clear();
    }

    /// <summary>
    /// Get count of stored assets across all scopes (for testing).
    /// </summary>
    public int Count => _scopedAssets.Values.Sum(scopeDict => scopeDict.Count);

    /// <summary>
    /// Get count of stored assets in a specific scope (for testing).
    /// </summary>
    public int CountInScope(string scope)
    {
        return _scopedAssets.TryGetValue(scope, out var scopeDict)
            ? scopeDict.Count
            : 0;
    }

    /// <summary>
    /// Check if asset exists in a specific scope (for testing).
    /// </summary>
    public bool Contains(string scope, string contentId)
    {
        return _scopedAssets.TryGetValue(scope, out var scopeDict) &&
               scopeDict.ContainsKey(contentId);
    }
}

namespace HPD.Agent;

/// <summary>
/// Local file system storage for binary assets.
/// Stores assets as individual files in a directory structure organized by scope.
/// </summary>
/// <remarks>
/// <para><b>Storage Layout:</b></para>
/// <code>
/// basePath/
///   {scope}/
///     {contentId}.jpg    (JPEG images)
///     {contentId}.png    (PNG images)
///     {contentId}.mp3    (Audio files)
///     {contentId}.pdf    (PDF documents)
///     {contentId}.bin    (Unknown types)
/// </code>
/// <para><b>Features:</b></para>
/// <list type="bullet">
/// <item>Automatic directory creation</item>
/// <item>Content-type based file extensions</item>
/// <item>Thread-safe (file system handles concurrency)</item>
/// <item>Simple cleanup (just delete files)</item>
/// <item>Per-scope isolation (sessionId-based)</item>
/// </list>
/// <para><b>Limitations:</b></para>
/// <list type="bullet">
/// <item>No deduplication (same image uploaded twice = 2 files)</item>
/// <item>No automatic cleanup (manual deletion required)</item>
/// <item>Not suitable for distributed systems (local files only)</item>
/// </list>
/// </remarks>
public class LocalFileAssetStore : IAssetStore
{
    private readonly string _basePath;

    /// <summary>
    /// Create a new local file asset store.
    /// </summary>
    /// <param name="basePath">Base directory for asset storage</param>
    public LocalFileAssetStore(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));

        // Ensure base directory exists
        Directory.CreateDirectory(basePath);
    }

    // ═══════════════════════════════════════════════════════════════════
    // IContentStore Implementation
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<string> PutAsync(
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

        // Ensure scope directory exists
        var scopePath = Path.Combine(_basePath, actualScope);
        Directory.CreateDirectory(scopePath);

        // Generate unique asset ID
        var contentId = Guid.NewGuid().ToString("N");
        var extension = GetExtensionFromContentType(contentType);
        var filePath = Path.Combine(scopePath, $"{contentId}{extension}");

        // Write to disk
        await File.WriteAllBytesAsync(filePath, data, cancellationToken);

        // If metadata is provided, write it to a companion .meta file
        if (metadata != null)
        {
            var metaPath = Path.Combine(scopePath, $"{contentId}.meta");
            var metaJson = System.Text.Json.JsonSerializer.Serialize(metadata);
            await File.WriteAllTextAsync(metaPath, metaJson, cancellationToken);
        }

        return contentId;
    }

    /// <inheritdoc />
    public async Task<ContentData?> GetAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return null;

        // Use "global" as default scope if null
        var actualScope = scope ?? "global";
        var scopePath = Path.Combine(_basePath, actualScope);

        if (!Directory.Exists(scopePath))
            return null;

        // Find file with this content ID (extension may vary)
        var files = Directory.GetFiles(scopePath, $"{contentId}.*")
            .Where(f => !f.EndsWith(".meta")) // Exclude metadata files
            .ToArray();

        if (files.Length == 0)
            return null;

        // Read first match (should only be one)
        var filePath = files[0];
        var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var contentType = GetContentTypeFromExtension(Path.GetExtension(filePath));
        var fileInfo = new FileInfo(filePath);

        // Try to read metadata from companion .meta file
        ContentMetadata? metadata = null;
        var metaPath = Path.Combine(scopePath, $"{contentId}.meta");
        if (File.Exists(metaPath))
        {
            try
            {
                var metaJson = await File.ReadAllTextAsync(metaPath, cancellationToken);
                metadata = System.Text.Json.JsonSerializer.Deserialize<ContentMetadata>(metaJson);
            }
            catch
            {
                // Ignore metadata read errors
            }
        }

        // Map to ContentData
        return new ContentData
        {
            Id = contentId,
            Data = data,
            ContentType = contentType,
            Info = new ContentInfo
            {
                Id = contentId,
                Name = metadata?.Name ?? contentId,
                ContentType = contentType,
                SizeBytes = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                Origin = metadata?.Origin ?? ContentSource.User,
                Description = metadata?.Description,
                LastModified = fileInfo.LastWriteTimeUtc,
                LastAccessed = null, // File system doesn't reliably track this
                Tags = metadata?.Tags,
                OriginalSource = metadata?.OriginalSource,
                ExtendedMetadata = null
            }
        };
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
        var scopePath = Path.Combine(_basePath, actualScope);

        if (!Directory.Exists(scopePath))
            return Task.CompletedTask;

        // Find and delete all files with this content ID (including .meta)
        var files = Directory.GetFiles(scopePath, $"{contentId}.*");

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Ignore errors (file may not exist, permissions, etc.)
                // Delete is idempotent - no need to throw
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ContentInfo>> QueryAsync(
        string? scope = null,
        ContentQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<string> scopePaths;

        if (scope == null)
        {
            // Query across ALL scopes
            if (!Directory.Exists(_basePath))
            {
                return Task.FromResult<IReadOnlyList<ContentInfo>>(Array.Empty<ContentInfo>());
            }
            scopePaths = Directory.GetDirectories(_basePath);
        }
        else
        {
            // Query within specific scope
            var scopePath = Path.Combine(_basePath, scope);
            if (!Directory.Exists(scopePath))
            {
                return Task.FromResult<IReadOnlyList<ContentInfo>>(Array.Empty<ContentInfo>());
            }
            scopePaths = new[] { scopePath };
        }

        // Collect all asset files across scopes
        var allFiles = scopePaths
            .SelectMany(scopePath => Directory.Exists(scopePath) ? Directory.GetFiles(scopePath) : Array.Empty<string>())
            .Where(f => !f.EndsWith(".meta")); // Exclude metadata files

        // Group by content ID (files may have different extensions)
        var assetGroups = allFiles
            .GroupBy(f => Path.GetFileNameWithoutExtension(f))
            .Select(g => g.First()); // Take first file for each content ID

        // Build ContentInfo for each asset
        var results = assetGroups.Select(filePath =>
        {
            var contentId = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var contentType = GetContentTypeFromExtension(extension);
            var fileInfo = new FileInfo(filePath);

            // Try to read metadata from companion .meta file
            ContentMetadata? metadata = null;
            var metaPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"{contentId}.meta");
            if (File.Exists(metaPath))
            {
                try
                {
                    var metaJson = File.ReadAllText(metaPath);
                    metadata = System.Text.Json.JsonSerializer.Deserialize<ContentMetadata>(metaJson);
                }
                catch
                {
                    // Ignore metadata read errors
                }
            }

            return new ContentInfo
            {
                Id = contentId,
                Name = metadata?.Name ?? contentId,
                ContentType = contentType,
                SizeBytes = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc,
                Origin = metadata?.Origin ?? ContentSource.User,
                Description = metadata?.Description,
                LastModified = fileInfo.LastWriteTimeUtc,
                LastAccessed = null, // File system doesn't reliably track this
                Tags = metadata?.Tags,
                OriginalSource = metadata?.OriginalSource,
                ExtendedMetadata = null
            };
        }).AsEnumerable();

        // Apply filters (ContentType, CreatedAfter, Limit)
        if (query?.ContentType != null)
        {
            results = results.Where(info =>
                info.ContentType.Equals(query.ContentType, StringComparison.OrdinalIgnoreCase));
        }

        if (query?.CreatedAfter != null)
        {
            results = results.Where(info => info.CreatedAt >= query.CreatedAfter.Value);
        }

        // Apply limit
        if (query?.Limit != null)
        {
            results = results.Take(query.Limit.Value);
        }

        return Task.FromResult<IReadOnlyList<ContentInfo>>(results.ToList());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Map content type to file extension.
    /// </summary>
    private static string GetExtensionFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "audio/wav" => ".wav",
            "audio/mp3" => ".mp3",
            "audio/mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/flac" => ".flac",
            "video/mp4" => ".mp4",
            "video/mpeg" => ".mpeg",
            "video/webm" => ".webm",
            "application/pdf" => ".pdf",
            "application/json" => ".json",
            "application/xml" => ".xml",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            _ => ".bin"
        };
    }

    /// <summary>
    /// Map file extension to content type.
    /// </summary>
    private static string GetContentTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" => "image/tiff",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mp3",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            ".mp4" => "video/mp4",
            ".mpeg" => "video/mpeg",
            ".webm" => "video/webm",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            _ => "application/octet-stream"
        };
    }
}

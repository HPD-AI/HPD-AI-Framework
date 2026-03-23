using System.Security.Cryptography;

namespace HPD.Agent;

/// <summary>
/// Local file system implementation of IContentStore.
/// Stores content as individual files in a directory structure organized by scope.
/// Supports folder-based organization via metadata tags, named upsert, and full ContentQuery filtering.
/// </summary>
/// <remarks>
/// <para><b>Storage Layout:</b></para>
/// <code>
/// basePath/
///   {scope}/
///     {contentId}.jpg    (JPEG images)
///     {contentId}.png    (PNG images)
///     {contentId}.md     (Markdown files)
///     {contentId}.txt    (Text files)
///     {contentId}.bin    (Unknown types)
///     {contentId}.meta   (JSON metadata companion file)
/// </code>
/// <para><b>Named Upsert Semantics:</b></para>
/// <para>
/// When ContentMetadata.Name is provided, PutAsync behaves as an upsert keyed on (scope, Name):
/// - Same name + same content hash → no-op, returns existing ID
/// - Same name + different content → overwrites in place, returns same ID
/// - No name → always inserts as a new entry with a generated ID
/// </para>
/// </remarks>
public class LocalFileContentStore : IContentStore
{
    private readonly string _basePath;
    // Name index file per scope: scope/.nameindex (JSON: name -> contentId)
    private readonly object _writeLock = new();

    /// <summary>Create a new local file content store.</summary>
    /// <param name="basePath">Base directory for content storage</param>
    public LocalFileContentStore(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
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

        var actualScope = scope ?? "global";
        var scopePath = Path.Combine(_basePath, SanitizePath(actualScope));
        Directory.CreateDirectory(scopePath);

        var name = metadata?.Name;

        if (name != null)
        {
            // Named upsert — check index under a lock to avoid races
            lock (_writeLock)
            {
                var nameIndex = ReadNameIndex(scopePath);
                if (nameIndex.TryGetValue(name, out var existingId))
                {
                    var existingMeta = ReadMetaFile(scopePath, existingId);
                    var newHash = ComputeHash(data);
                    var existingHash = existingMeta?.GetValueOrDefault("contentHash") as string;
                    if (existingHash == newHash)
                    {
                        // Same content — no-op
                        return existingId;
                    }
                    else
                    {
                        // Overwrite in place
                        var existingFile = FindContentFile(scopePath, existingId);
                        var newExt = GetExtensionFromContentType(contentType);
                        var newFilePath = Path.Combine(scopePath, $"{existingId}{newExt}");

                        // Remove old file if extension changed
                        if (existingFile != null && existingFile != newFilePath)
                            File.Delete(existingFile);

                        File.WriteAllBytes(newFilePath, data);
                        WriteMetaFile(scopePath, existingId, contentType, metadata, newHash);
                        return existingId;
                    }
                }

                // New named entry
                var newId = Guid.NewGuid().ToString("N");
                var hash = ComputeHash(data);
                var ext = GetExtensionFromContentType(contentType);
                var filePath = Path.Combine(scopePath, $"{newId}{ext}");
                File.WriteAllBytes(filePath, data);
                WriteMetaFile(scopePath, newId, contentType, metadata, hash);
                nameIndex[name] = newId;
                WriteNameIndex(scopePath, nameIndex);
                return newId;
            }
        }

        // Unnamed insert — always creates a new entry
        var id = Guid.NewGuid().ToString("N");
        var extension = GetExtensionFromContentType(contentType);
        var contentFilePath = Path.Combine(scopePath, $"{id}{extension}");
        await File.WriteAllBytesAsync(contentFilePath, data, cancellationToken);
        WriteMetaFile(scopePath, id, contentType, metadata, null);
        return id;
    }

    /// <inheritdoc />
    public async Task<ContentData?> GetAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return null;

        var actualScope = scope ?? "global";
        var scopePath = Path.Combine(_basePath, SanitizePath(actualScope));

        if (!Directory.Exists(scopePath))
            return null;

        var filePath = FindContentFile(scopePath, contentId);
        if (filePath == null)
            return null;

        var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var contentType = GetContentTypeFromExtension(Path.GetExtension(filePath));
        var fileInfo = new FileInfo(filePath);
        var metaRaw = ReadMetaFile(scopePath, contentId);
        var metadata = DeserializeMetadata(metaRaw);

        return new ContentData
        {
            Id = contentId,
            Data = data,
            ContentType = contentType,
            Info = BuildContentInfo(contentId, contentType, fileInfo.Length,
                fileInfo.CreationTimeUtc, fileInfo.LastWriteTimeUtc, metadata, metaRaw)
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

        var actualScope = scope ?? "global";
        var scopePath = Path.Combine(_basePath, SanitizePath(actualScope));

        if (!Directory.Exists(scopePath))
            return Task.CompletedTask;

        lock (_writeLock)
        {
            // Read metadata to find name before deleting
            var metaRaw = ReadMetaFile(scopePath, contentId);
            var name = metaRaw?.GetValueOrDefault("name") as string;

            // Delete content + meta files
            foreach (var file in Directory.GetFiles(scopePath, $"{contentId}.*"))
            {
                try { File.Delete(file); } catch { }
            }

            // Remove from name index
            if (name != null)
            {
                var nameIndex = ReadNameIndex(scopePath);
                if (nameIndex.Remove(name))
                    WriteNameIndex(scopePath, nameIndex);
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
            if (!Directory.Exists(_basePath))
                return Task.FromResult<IReadOnlyList<ContentInfo>>(Array.Empty<ContentInfo>());
            scopePaths = Directory.GetDirectories(_basePath);
        }
        else
        {
            var scopePath = Path.Combine(_basePath, SanitizePath(scope));
            if (!Directory.Exists(scopePath))
                return Task.FromResult<IReadOnlyList<ContentInfo>>(Array.Empty<ContentInfo>());
            scopePaths = new[] { scopePath };
        }

        var results = scopePaths
            .SelectMany(sp => Directory.Exists(sp) ? Directory.GetFiles(sp) : Array.Empty<string>())
            .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex"))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f))
            .Select(g => g.First())
            .Select(filePath =>
            {
                var contentId = Path.GetFileNameWithoutExtension(filePath);
                var contentType = GetContentTypeFromExtension(Path.GetExtension(filePath));
                var fileInfo = new FileInfo(filePath);
                var metaRaw = ReadMetaFile(Path.GetDirectoryName(filePath)!, contentId);
                var metadata = DeserializeMetadata(metaRaw);
                return BuildContentInfo(contentId, contentType, fileInfo.Length,
                    fileInfo.CreationTimeUtc, fileInfo.LastWriteTimeUtc, metadata, metaRaw);
            })
            .AsEnumerable();

        // Apply filters
        if (query?.ContentType != null)
            results = results.Where(i => i.ContentType.Equals(query.ContentType, StringComparison.OrdinalIgnoreCase));

        if (query?.CreatedAfter != null)
            results = results.Where(i => i.CreatedAt >= query.CreatedAfter.Value);

        if (query?.Tags != null)
        {
            results = results.Where(i =>
                i.Tags != null &&
                query.Tags.All(kv => i.Tags.TryGetValue(kv.Key, out var v) && v == kv.Value));
        }

        if (query?.Name != null)
            results = results.Where(i => i.Name.Equals(query.Name, StringComparison.OrdinalIgnoreCase));

        if (query?.Limit != null)
            results = results.Take(query.Limit.Value);

        return Task.FromResult<IReadOnlyList<ContentInfo>>(results.ToList());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Metadata Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static void WriteMetaFile(string scopePath, string contentId, string contentType,
        ContentMetadata? metadata, string? contentHash)
    {
        var dict = new Dictionary<string, object> { ["contentType"] = contentType };
        if (metadata?.Name != null) dict["name"] = metadata.Name;
        if (metadata?.Description != null) dict["description"] = metadata.Description;
        if (metadata?.Origin != null) dict["origin"] = metadata.Origin.ToString()!;
        if (metadata?.OriginalSource != null) dict["originalSource"] = metadata.OriginalSource;
        if (metadata?.Tags != null) dict["tags"] = metadata.Tags;
        if (contentHash != null) dict["contentHash"] = contentHash;

        var metaPath = Path.Combine(scopePath, $"{contentId}.meta");
        File.WriteAllText(metaPath, System.Text.Json.JsonSerializer.Serialize(dict));
    }

    private static Dictionary<string, object>? ReadMetaFile(string scopePath, string contentId)
    {
        var metaPath = Path.Combine(scopePath, $"{contentId}.meta");
        if (!File.Exists(metaPath)) return null;
        try
        {
            var json = File.ReadAllText(metaPath);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch { return null; }
    }

    private static ContentMetadata? DeserializeMetadata(Dictionary<string, object>? raw)
    {
        if (raw == null) return null;

        IReadOnlyDictionary<string, string>? tags = null;
        if (raw.TryGetValue("tags", out var tagsObj) && tagsObj is System.Text.Json.JsonElement tagsEl)
        {
            var d = new Dictionary<string, string>();
            foreach (var prop in tagsEl.EnumerateObject())
                d[prop.Name] = prop.Value.GetString() ?? "";
            tags = d;
        }

        ContentSource? origin = null;
        if (raw.TryGetValue("origin", out var originObj) &&
            Enum.TryParse<ContentSource>(originObj?.ToString(), out var parsed))
            origin = parsed;

        return new ContentMetadata
        {
            Name = raw.GetValueOrDefault("name")?.ToString(),
            Description = raw.GetValueOrDefault("description")?.ToString(),
            OriginalSource = raw.GetValueOrDefault("originalSource")?.ToString(),
            Origin = origin,
            Tags = tags
        };
    }

    private static ContentInfo BuildContentInfo(string contentId, string contentType, long sizeBytes,
        DateTime createdAt, DateTime lastModified, ContentMetadata? metadata, Dictionary<string, object>? metaRaw)
    {
        var hash = metaRaw?.GetValueOrDefault("contentHash")?.ToString();
        var extendedMeta = hash != null
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["contentHash"] = hash }
            : null;

        return new ContentInfo
        {
            Id = contentId,
            Name = metadata?.Name ?? contentId,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            CreatedAt = createdAt,
            LastModified = lastModified,
            LastAccessed = null,
            Origin = metadata?.Origin ?? ContentSource.User,
            Description = metadata?.Description,
            Tags = metadata?.Tags,
            OriginalSource = metadata?.OriginalSource,
            ExtendedMetadata = extendedMeta
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // Name Index (scope/.nameindex JSON file)
    // ═══════════════════════════════════════════════════════════════════

    private static Dictionary<string, string> ReadNameIndex(string scopePath)
    {
        var indexPath = Path.Combine(scopePath, ".nameindex");
        if (!File.Exists(indexPath)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = File.ReadAllText(indexPath);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static void WriteNameIndex(string scopePath, Dictionary<string, string> index)
    {
        var indexPath = Path.Combine(scopePath, ".nameindex");
        File.WriteAllText(indexPath, System.Text.Json.JsonSerializer.Serialize(index));
    }

    // ═══════════════════════════════════════════════════════════════════
    // File Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static string? FindContentFile(string scopePath, string contentId)
    {
        var files = Directory.GetFiles(scopePath, $"{contentId}.*")
            .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex"))
            .ToArray();
        return files.Length > 0 ? files[0] : null;
    }

    private static string SanitizePath(string segment) =>
        string.Join("_", segment.Split(Path.GetInvalidFileNameChars()));

    private static string ComputeHash(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string GetExtensionFromContentType(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "audio/wav" => ".wav",
            "audio/mp3" or "audio/mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/flac" => ".flac",
            "video/mp4" => ".mp4",
            "video/mpeg" => ".mpeg",
            "video/webm" => ".webm",
            "application/pdf" => ".pdf",
            "application/json" => ".json",
            "application/xml" => ".xml",
            "text/plain" => ".txt",
            "text/markdown" => ".md",
            "text/csv" => ".csv",
            _ => ".bin"
        };

    private static string GetContentTypeFromExtension(string extension) =>
        extension.ToLowerInvariant() switch
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
            ".md" => "text/markdown",
            ".csv" => "text/csv",
            _ => "application/octet-stream"
        };
}

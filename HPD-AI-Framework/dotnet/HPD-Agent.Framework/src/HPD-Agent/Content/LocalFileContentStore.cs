using System.Security.Cryptography;

namespace HPD.Agent;

/// <summary>
/// Local file system implementation of IContentStore.
/// Stores content as individual files in a directory structure organized by scope.
/// Supports metadata-based organization, explicit versioned writes, and full ContentQuery filtering.
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

        var actualScope = scope ?? "global";
        var scopePath = Path.Combine(_basePath, SanitizePath(actualScope));
        Directory.CreateDirectory(scopePath);

        var (tempPath, contentHash, sizeBytes) = await WriteStreamToTempFileAsync(scopePath, data, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            lock (_writeLock)
            {
                return options.Mode switch
                {
                    ContentWriteMode.Create or ContentWriteMode.Stage => Create(scopePath, tempPath, contentType, metadata, contentHash, sizeBytes, options),
                    ContentWriteMode.ReplaceById => ReplaceById(scopePath, tempPath, contentType, metadata, contentHash, options),
                    ContentWriteMode.ReplaceByName => ReplaceByName(scopePath, tempPath, contentType, metadata, contentHash, options),
                    ContentWriteMode.Append => Append(scopePath, tempPath, contentType, metadata, contentHash, options),
                    _ => throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unsupported content write mode.")
                };
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
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
        var scopePath = Path.Combine(_basePath, SanitizePath(actualScope));

        if (!Directory.Exists(scopePath))
            return Task.FromResult<Stream?>(null);

        var filePath = FindContentFile(scopePath, contentId);
        if (filePath == null)
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);
        return Task.FromResult<Stream?>(stream);
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
        var scopePath = Path.Combine(_basePath, SanitizePath(actualScope));
        if (!Directory.Exists(scopePath))
            return Task.FromResult<ContentInfo?>(null);

        return Task.FromResult(BuildContentInfoFromId(scopePath, contentId));
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
        var scopePath = Path.Combine(_basePath, SanitizePath(actualScope));

        if (!Directory.Exists(scopePath))
            return Task.CompletedTask;

        lock (_writeLock)
        {
            // Read metadata to find name before deleting
            var metaRaw = ReadMetaFile(scopePath, contentId);
            if (options?.IfMatchVersion != null && metaRaw?.Version != options.IfMatchVersion)
                throw new ContentConflictException(
                    $"Content '{contentId}' version conflict.",
                    contentId,
                    options.IfMatchVersion,
                    metaRaw?.Version);

            // Delete content + meta files
            foreach (var file in Directory.GetFiles(scopePath, $"{contentId}.*"))
            {
                try { File.Delete(file); } catch { }
            }

            // Remove from name index
            if (metaRaw?.Name != null)
            {
                var nameIndex = ReadNameIndex(scopePath);
                if (nameIndex.Remove(MakeNameKey(metaRaw.Name, metaRaw.Tags)))
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
            .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex") && !f.EndsWith(".tmp"))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f))
            .Select(g => g.First())
            .Select(filePath =>
            {
                var contentId = Path.GetFileNameWithoutExtension(filePath);
                var fileInfo = new FileInfo(filePath);
                var metaRaw = ReadMetaFile(Path.GetDirectoryName(filePath)!, contentId);
                var contentType = metaRaw?.ContentType ?? GetContentTypeFromExtension(Path.GetExtension(filePath));
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

    private static ContentInfo Create(
        string scopePath,
        string tempPath,
        string contentType,
        ContentMetadata metadata,
        string contentHash,
        long sizeBytes,
        ContentWriteOptions options)
    {
        var nameIndex = ReadNameIndex(scopePath);
        if (options.FailIfNameExists &&
            metadata.Name != null &&
            nameIndex.ContainsKey(MakeNameKey(metadata)))
        {
            throw new ContentConflictException($"Content named '{metadata.Name}' already exists.");
        }

        var id = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(scopePath, $"{id}{GetExtensionFromContentType(contentType)}");
        File.Move(tempPath, filePath, overwrite: false);
        WriteMetaFile(scopePath, id, contentType, metadata, contentHash, NewVersion());
        if (metadata.Name != null)
        {
            nameIndex[MakeNameKey(metadata)] = id;
            WriteNameIndex(scopePath, nameIndex);
        }

        var fileInfo = new FileInfo(filePath);
        return BuildContentInfo(id, contentType, sizeBytes,
            fileInfo.CreationTimeUtc, fileInfo.LastWriteTimeUtc, metadata, ReadMetaFile(scopePath, id));
    }

    private static ContentInfo ReplaceById(
        string scopePath,
        string tempPath,
        string contentType,
        ContentMetadata metadata,
        string contentHash,
        ContentWriteOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ContentId))
            throw new ArgumentException("ReplaceById requires ContentWriteOptions.ContentId.", nameof(options));

        var contentId = options.ContentId;
        var existingMeta = ReadMetaFile(scopePath, contentId);
        if (existingMeta == null || FindContentFile(scopePath, contentId) == null)
            throw new FileNotFoundException($"Content '{contentId}' was not found.");

        EnsureVersionMatches(contentId, existingMeta.Version, options.IfMatchVersion);

        var existingFile = FindContentFile(scopePath, contentId);
        var newFilePath = Path.Combine(scopePath, $"{contentId}{GetExtensionFromContentType(contentType)}");
        if (existingFile != null && existingFile != newFilePath)
            File.Delete(existingFile);

        File.Move(tempPath, newFilePath, overwrite: true);
        WriteMetaFile(scopePath, contentId, contentType, metadata, contentHash, NewVersion());

        var nameIndex = ReadNameIndex(scopePath);
        if (existingMeta.Name != null)
            nameIndex.Remove(MakeNameKey(existingMeta.Name, existingMeta.Tags));
        if (metadata.Name != null)
            nameIndex[MakeNameKey(metadata)] = contentId;
        WriteNameIndex(scopePath, nameIndex);

        return BuildContentInfoFromId(scopePath, contentId)!;
    }

    private static ContentInfo ReplaceByName(
        string scopePath,
        string tempPath,
        string contentType,
        ContentMetadata metadata,
        string contentHash,
        ContentWriteOptions options)
    {
        if (string.IsNullOrWhiteSpace(metadata.Name))
            throw new ArgumentException("ReplaceByName requires ContentMetadata.Name.", nameof(metadata));

        var nameIndex = ReadNameIndex(scopePath);
        if (!nameIndex.TryGetValue(MakeNameKey(metadata), out var contentId))
            throw new FileNotFoundException($"Content named '{metadata.Name}' was not found.");

        return ReplaceById(scopePath, tempPath, contentType, metadata, contentHash, options with { ContentId = contentId });
    }

    private static ContentInfo Append(
        string scopePath,
        string tempPath,
        string contentType,
        ContentMetadata metadata,
        string contentHash,
        ContentWriteOptions options)
    {
        string? contentId = options.ContentId;
        if (contentId == null && metadata.Name != null)
        {
            var nameIndex = ReadNameIndex(scopePath);
            nameIndex.TryGetValue(MakeNameKey(metadata), out contentId);
        }

        if (contentId == null || FindContentFile(scopePath, contentId) == null)
            return Create(scopePath, tempPath, contentType, metadata, contentHash, new FileInfo(tempPath).Length, options with { Mode = ContentWriteMode.Create });

        var existingMeta = ReadMetaFile(scopePath, contentId);
        EnsureVersionMatches(contentId, existingMeta?.Version, options.IfMatchVersion);

        var existingFile = FindContentFile(scopePath, contentId)!;
        using (var output = new FileStream(existingFile, FileMode.Append, FileAccess.Write, FileShare.None))
        using (var input = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            input.CopyTo(output);
        }

        File.Delete(tempPath);
        var appendedHash = ComputeHashFile(existingFile);
        WriteMetaFile(scopePath, contentId, contentType, metadata, appendedHash, NewVersion());
        return BuildContentInfoFromId(scopePath, contentId)!;
    }

    private static void WriteMetaFile(string scopePath, string contentId, string contentType,
        ContentMetadata? metadata, string? contentHash, string version)
    {
        var meta = new LocalContentMetadata(
            ContentType: contentType,
            Version: version,
            Name: metadata?.Name,
            Description: metadata?.Description,
            Origin: metadata?.Origin?.ToString(),
            OriginalSource: metadata?.OriginalSource,
            Tags: metadata?.Tags is null
                ? null
                : new Dictionary<string, string>(metadata.Tags, StringComparer.Ordinal),
            ContentHash: contentHash);
        var metaPath = Path.Combine(scopePath, $"{contentId}.meta");
        File.WriteAllText(metaPath, System.Text.Json.JsonSerializer.Serialize(meta, HPDJsonContext.Default.LocalContentMetadata));
    }

    private static LocalContentMetadata? ReadMetaFile(string scopePath, string contentId)
    {
        var metaPath = Path.Combine(scopePath, $"{contentId}.meta");
        if (!File.Exists(metaPath)) return null;
        try
        {
            var json = File.ReadAllText(metaPath);
            return System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.LocalContentMetadata);
        }
        catch { return null; }
    }

    private static ContentMetadata? DeserializeMetadata(LocalContentMetadata? raw)
    {
        if (raw == null) return null;

        ContentSource? origin = null;
        if (raw.Origin is not null &&
            Enum.TryParse<ContentSource>(raw.Origin, out var parsed))
            origin = parsed;

        return new ContentMetadata
        {
            ContentType = raw.ContentType,
            Name = raw.Name,
            Description = raw.Description,
            OriginalSource = raw.OriginalSource,
            Origin = origin,
            Tags = raw.Tags
        };
    }

    private static ContentInfo BuildContentInfo(string contentId, string contentType, long sizeBytes,
        DateTime createdAt, DateTime lastModified, ContentMetadata? metadata, LocalContentMetadata? metaRaw)
    {
        var hash = metaRaw?.ContentHash;
        var extendedMeta = hash != null
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["contentHash"] = hash }
            : null;

        return new ContentInfo
        {
            Id = contentId,
            Version = metaRaw?.Version ?? "legacy:unknown",
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

    private static ContentInfo? BuildContentInfoFromId(string scopePath, string contentId)
    {
        var filePath = FindContentFile(scopePath, contentId);
        if (filePath == null)
            return null;

        var fileInfo = new FileInfo(filePath);
        var metaRaw = ReadMetaFile(scopePath, contentId);
        var contentType = metaRaw?.ContentType ?? GetContentTypeFromExtension(Path.GetExtension(filePath));
        var metadata = DeserializeMetadata(metaRaw);
        return BuildContentInfo(contentId, contentType, fileInfo.Length,
            fileInfo.CreationTimeUtc, fileInfo.LastWriteTimeUtc, metadata, metaRaw);
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
            return System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.DictionaryStringString)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static void WriteNameIndex(string scopePath, Dictionary<string, string> index)
    {
        var indexPath = Path.Combine(scopePath, ".nameindex");
        File.WriteAllText(indexPath, System.Text.Json.JsonSerializer.Serialize(index, HPDJsonContext.Default.DictionaryStringString));
    }

    private static string MakeNameKey(ContentMetadata metadata) => MakeNameKey(metadata.Name, metadata.Tags);

    private static string MakeNameKey(string? name, IReadOnlyDictionary<string, string>? tags)
    {
        var kind = tags != null && tags.TryGetValue("kind", out var value)
            ? value.Trim().Trim('/')
            : "";
        return $"{kind}/{name}".ToLowerInvariant();
    }

    // ═══════════════════════════════════════════════════════════════════
    // File Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static string? FindContentFile(string scopePath, string contentId)
    {
        var files = Directory.GetFiles(scopePath, $"{contentId}.*")
            .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex") && !f.EndsWith(".tmp"))
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

    private static string ComputeHashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static void EnsureVersionMatches(string contentId, string? actualVersion, string? expectedVersion)
    {
        if (expectedVersion != null && actualVersion != expectedVersion)
        {
            throw new ContentConflictException(
                $"Content '{contentId}' version conflict.",
                contentId,
                expectedVersion,
                actualVersion);
        }
    }

    private static string NewVersion() => $"rev:{Guid.NewGuid():N}";

    private static async Task<(string TempPath, string ContentHash, long SizeBytes)> WriteStreamToTempFileAsync(
        string scopePath,
        Stream data,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(scopePath, $".upload-{Guid.NewGuid():N}.tmp");
        await using (var output = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await data.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        var info = new FileInfo(tempPath);
        var hash = await ComputeHashFileAsync(tempPath, cancellationToken).ConfigureAwait(false);
        return (tempPath, hash, info.Length);
    }

    private static async Task<string> ComputeHashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
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

public sealed record LocalContentMetadata(
    string ContentType,
    string? Version = null,
    string? Name = null,
    string? Description = null,
    string? Origin = null,
    string? OriginalSource = null,
    Dictionary<string, string>? Tags = null,
    string? ContentHash = null);

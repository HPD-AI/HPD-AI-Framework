using System.Security.Cryptography;
using System.Collections.Concurrent;

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
///     {contentId}.{generation}.bin (immutable content generation)
///     {contentId}.meta             (atomically replaced current-generation pointer)
///     .hpd-store.lock              (cross-process scope mutation lock)
/// </code>
/// </remarks>
public class LocalFileContentStore : IContentStore
{
    private readonly string _basePath;
    private static readonly ConcurrentDictionary<string, object> StoreLocks = new(StringComparer.Ordinal);
    // Name index file per scope: scope/.nameindex (JSON: name -> contentId)
    private readonly object _writeLock;

    /// <summary>Create a new local file content store.</summary>
    /// <param name="basePath">Base directory for content storage</param>
    public LocalFileContentStore(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        _basePath = Path.GetFullPath(basePath);
        _writeLock = StoreLocks.GetOrAdd(_basePath, static _ => new object());
        Directory.CreateDirectory(_basePath);
    }

    // ═══════════════════════════════════════════════════════════════════
    // IContentStore Implementation
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask<ContentInfo> WriteAsync(
        ContentScope scope,
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

        ValidateScope(scope);
        var scopePath = GetScopePath(scope);
        Directory.CreateDirectory(scopePath);

        var (tempPath, contentHash, sizeBytes) = await WriteStreamToTempFileAsync(scopePath, data, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            lock (_writeLock)
            {
                using var scopeLock = AcquireScopeLock(scopePath, cancellationToken);
                return options.Mode switch
                {
                    ContentWriteMode.Create => Create(scope, scopePath, tempPath, contentType, metadata, contentHash, sizeBytes, options),
                    ContentWriteMode.ReplaceById => ReplaceById(scope, scopePath, tempPath, contentType, metadata, contentHash, options),
                    ContentWriteMode.ReplaceByName => ReplaceByName(scope, scopePath, tempPath, contentType, metadata, contentHash, options),
                    ContentWriteMode.Append => Append(scope, scopePath, tempPath, contentType, metadata, contentHash, options),
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
    public ValueTask<ContentReadResult?> OpenReadAsync(
        ContentAddress address,
        CancellationToken cancellationToken = default)
    {
        ValidateAddress(address);
        var scopePath = GetScopePath(address.Scope);

        if (!Directory.Exists(scopePath))
            return ValueTask.FromResult<ContentReadResult?>(null);

        lock (_writeLock)
        {
            using var scopeLock = AcquireScopeLock(scopePath, cancellationToken);
            var filePath = FindContentFile(scopePath, address.ContentId);
            if (filePath == null)
                return ValueTask.FromResult<ContentReadResult?>(null);

            var info = BuildContentInfoFromId(address.Scope, scopePath, address.ContentId);
            if (info is null)
                return ValueTask.FromResult<ContentReadResult?>(null);
            EnsureAddressMatches(address, info.Address);

            Stream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 81920,
                useAsync: true);
            return ValueTask.FromResult<ContentReadResult?>(new ContentReadResult
            {
                Content = stream,
                Info = info
            });
        }
    }

    /// <inheritdoc />
    public ValueTask<Uri?> CreateReadUriAsync(
        ContentAddress address,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        ValidateAddress(address);
        return ValueTask.FromResult<Uri?>(null);
    }

    /// <inheritdoc />
    public ValueTask<ContentInfo?> StatAsync(
        ContentAddress address,
        CancellationToken cancellationToken = default)
    {
        ValidateAddress(address);
        var scopePath = GetScopePath(address.Scope);
        if (!Directory.Exists(scopePath))
            return ValueTask.FromResult<ContentInfo?>(null);

        lock (_writeLock)
        {
            using var scopeLock = AcquireScopeLock(scopePath, cancellationToken);
            var info = BuildContentInfoFromId(address.Scope, scopePath, address.ContentId);
            if (info is not null)
                EnsureAddressMatches(address, info.Address);
            return ValueTask.FromResult(info);
        }
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(
        ContentAddress address,
        CancellationToken cancellationToken = default)
    {
        ValidateAddress(address);
        var contentId = address.ContentId;
        var scopePath = GetScopePath(address.Scope);

        if (!Directory.Exists(scopePath))
            return ValueTask.CompletedTask;

        lock (_writeLock)
        {
            using var scopeLock = AcquireScopeLock(scopePath, cancellationToken);
            // Read metadata to find name before deleting
            var metaRaw = ReadMetaFile(scopePath, contentId);
            if (metaRaw is not null)
                EnsureAddressMatches(address, new ContentAddress(address.Scope, contentId, metaRaw.Version, metaRaw.ContentHash));

            // Remove the authoritative pointer first. A crash can leave orphaned immutable
            // generations, but can never leave a published pointer to reclaimed bytes.
            TryDelete(Path.Combine(scopePath, $"{contentId}.meta"));
            foreach (var file in Directory.GetFiles(scopePath, $"{contentId}.*")
                .Where(file => !file.EndsWith(".meta", StringComparison.Ordinal)))
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

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContentInfo>> QueryAsync(
        ContentScope scope,
        ContentQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        var scopePath = GetScopePath(scope);
        if (!Directory.Exists(scopePath))
            return ValueTask.FromResult<IReadOnlyList<ContentInfo>>(Array.Empty<ContentInfo>());
        IEnumerable<string> scopePaths = new[] { scopePath };

        List<ContentInfo> snapshot;
        lock (_writeLock)
        {
            using var scopeLock = AcquireScopeLock(scopePath, cancellationToken);
            snapshot = scopePaths
                .SelectMany(sp => Directory.Exists(sp) ? Directory.GetFiles(sp, "*.meta") : Array.Empty<string>())
                .Select(metaPath =>
                {
                    var contentId = Path.GetFileNameWithoutExtension(metaPath);
                    var filePath = FindContentFile(Path.GetDirectoryName(metaPath)!, contentId)
                        ?? throw new InvalidDataException($"Content '{contentId}' current generation is unavailable.");
                    var fileInfo = new FileInfo(filePath);
                    var metaRaw = ReadRequiredMetaFile(Path.GetDirectoryName(metaPath)!, contentId);
                    var contentType = metaRaw.ContentType;
                    var metadata = DeserializeMetadata(metaRaw);
                    return BuildContentInfo(scope, contentId, contentType, fileInfo.Length,
                        fileInfo.CreationTimeUtc, fileInfo.LastWriteTimeUtc, metadata, metaRaw);
                })
                .ToList();
        }

        IEnumerable<ContentInfo> results = snapshot;

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

        return ValueTask.FromResult<IReadOnlyList<ContentInfo>>(results.ToList());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Metadata Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static ContentInfo Create(
        ContentScope scope,
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
        var filePath = NewGenerationPath(scopePath, id, contentType);
        File.Move(tempPath, filePath, overwrite: false);
        WriteMetaFile(scopePath, id, contentType, metadata, contentHash, NewVersion(), Path.GetFileName(filePath));
        if (metadata.Name != null)
        {
            nameIndex[MakeNameKey(metadata)] = id;
            WriteNameIndex(scopePath, nameIndex);
        }

        var fileInfo = new FileInfo(filePath);
        return BuildContentInfo(scope, id, contentType, sizeBytes,
            fileInfo.CreationTimeUtc, fileInfo.LastWriteTimeUtc, metadata, ReadMetaFile(scopePath, id));
    }

    private static ContentInfo ReplaceById(
        ContentScope scope,
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

        var existingFile = FindContentFile(scopePath, contentId)!;
        var newFilePath = NewGenerationPath(scopePath, contentId, contentType);
        File.Move(tempPath, newFilePath, overwrite: false);
        WriteMetaFile(scopePath, contentId, contentType, metadata, contentHash, NewVersion(), Path.GetFileName(newFilePath));

        var nameIndex = ReadNameIndex(scopePath);
        if (existingMeta.Name != null)
            nameIndex.Remove(MakeNameKey(existingMeta.Name, existingMeta.Tags));
        if (metadata.Name != null)
            nameIndex[MakeNameKey(metadata)] = contentId;
        WriteNameIndex(scopePath, nameIndex);
        TryDelete(existingFile);

        return BuildContentInfoFromId(scope, scopePath, contentId)!;
    }

    private static ContentInfo ReplaceByName(
        ContentScope scope,
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

        return ReplaceById(scope, scopePath, tempPath, contentType, metadata, contentHash, options with { ContentId = contentId });
    }

    private static ContentInfo Append(
        ContentScope scope,
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
            return Create(scope, scopePath, tempPath, contentType, metadata, contentHash, new FileInfo(tempPath).Length, options with { Mode = ContentWriteMode.Create });

        var existingMeta = ReadMetaFile(scopePath, contentId);
        if (existingMeta == null)
            throw new InvalidDataException($"Content '{contentId}' is missing required metadata.");

        EnsureVersionMatches(contentId, existingMeta?.Version, options.IfMatchVersion);

        var existingFile = FindContentFile(scopePath, contentId)!;
        var combinedPath = Path.Combine(scopePath, $".append-{Guid.NewGuid():N}.tmp");
        using (var output = new FileStream(combinedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var existing = new FileStream(existingFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        using (var input = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            existing.CopyTo(output);
            input.CopyTo(output);
        }

        File.Delete(tempPath);
        var appendedHash = ComputeHashFile(combinedPath);
        return ReplaceById(scope, scopePath, combinedPath, contentType, metadata, appendedHash,
            options with { Mode = ContentWriteMode.ReplaceById, ContentId = contentId });
    }

    private static void WriteMetaFile(string scopePath, string contentId, string contentType,
        ContentMetadata? metadata, string? contentHash, string version, string dataFileName)
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
            ContentHash: contentHash,
            DataFileName: dataFileName);
        var metaPath = Path.Combine(scopePath, $"{contentId}.meta");
        WriteTextAtomically(metaPath,
            System.Text.Json.JsonSerializer.Serialize(meta, HPDJsonContext.Default.LocalContentMetadata));
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

    private static LocalContentMetadata ReadRequiredMetaFile(string scopePath, string contentId)
    {
        return ReadMetaFile(scopePath, contentId)
            ?? throw new InvalidDataException($"Content '{contentId}' is missing required metadata.");
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

    private static ContentInfo BuildContentInfo(ContentScope scope, string contentId, string contentType, long sizeBytes,
        DateTime createdAt, DateTime lastModified, ContentMetadata? metadata, LocalContentMetadata? metaRaw)
    {
        var hash = metaRaw?.ContentHash;
        var extendedMeta = hash != null
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["contentHash"] = hash }
            : null;

        return new ContentInfo
        {
            Address = new ContentAddress(
                scope,
                contentId,
                metaRaw.Version ?? throw new InvalidDataException($"Content '{contentId}' metadata is missing a version."),
                hash),
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

    private static ContentInfo? BuildContentInfoFromId(ContentScope scope, string scopePath, string contentId)
    {
        var filePath = FindContentFile(scopePath, contentId);
        if (filePath == null)
            return null;

        var fileInfo = new FileInfo(filePath);
        var metaRaw = ReadRequiredMetaFile(scopePath, contentId);
        var contentType = metaRaw.ContentType;
        var metadata = DeserializeMetadata(metaRaw);
        return BuildContentInfo(scope, contentId, contentType, fileInfo.Length,
            fileInfo.CreationTimeUtc, fileInfo.LastWriteTimeUtc, metadata, metaRaw);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Name Index (scope/.nameindex JSON file)
    // ═══════════════════════════════════════════════════════════════════

    private static Dictionary<string, string> ReadNameIndex(string scopePath)
    {
        // Metadata pointers are authoritative. Rebuilding avoids a separately committed
        // name-index file becoming catalog truth after a crash.
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var metaPath in Directory.GetFiles(scopePath, "*.meta"))
        {
            var id = Path.GetFileNameWithoutExtension(metaPath);
            var metadata = ReadMetaFile(scopePath, id);
            if (metadata?.Name is not null)
                result[MakeNameKey(metadata.Name, metadata.Tags)] = id;
        }
        return result;
    }

    private static void WriteNameIndex(string scopePath, Dictionary<string, string> index)
    {
        var indexPath = Path.Combine(scopePath, ".nameindex");
        WriteTextAtomically(indexPath,
            System.Text.Json.JsonSerializer.Serialize(index, HPDJsonContext.Default.DictionaryStringString));
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
        var metadata = ReadMetaFile(scopePath, contentId);
        if (!string.IsNullOrWhiteSpace(metadata?.DataFileName))
        {
            var candidate = Path.Combine(scopePath, metadata.DataFileName);
            return File.Exists(candidate) ? candidate : null;
        }

        // Legacy fallback for stores created before generation pointers.
        var files = Directory.GetFiles(scopePath, $"{contentId}.*")
            .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex") && !f.EndsWith(".tmp"))
            .ToArray();
        return files.Length > 0 ? files[0] : null;
    }

    private static string NewGenerationPath(string scopePath, string contentId, string contentType)
        => Path.Combine(scopePath,
            $"{contentId}.{Guid.NewGuid():N}{GetExtensionFromContentType(contentType)}");

    private static FileStream AcquireScopeLock(string scopePath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(scopePath, ".hpd-store.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(10));
            }
        }
    }

    private static void WriteTextAtomically(string destination, string content)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, options: FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private string GetScopePath(ContentScope scope)
    {
        ValidateScope(scope);
        return Path.Combine(_basePath, scope.Value);
    }

    private static void ValidateScope(ContentScope scope) => ValidateSegment(scope.Value, nameof(scope));

    private static void ValidateAddress(ContentAddress address)
    {
        ValidateScope(address.Scope);
        ValidateSegment(address.ContentId, nameof(address));
    }

    private static void ValidateSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Content scope and identifier values must be single, unmodified path segments.", parameterName);
        }
    }

    private static void EnsureAddressMatches(ContentAddress expected, ContentAddress actual)
    {
        EnsureVersionMatches(actual.ContentId, actual.Version, expected.Version);
        if (expected.Sha256 is not null &&
            !string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ContentConflictException(
                $"Content '{actual.ContentId}' hash conflict.",
                actual.ContentId,
                expected.Sha256,
                actual.Sha256);
        }
    }

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
    string? ContentHash = null,
    string? DataFileName = null);

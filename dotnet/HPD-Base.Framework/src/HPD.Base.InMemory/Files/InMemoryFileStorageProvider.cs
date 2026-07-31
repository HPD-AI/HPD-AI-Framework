using System.Collections.Concurrent;
using System.Security.Cryptography;
using HPD.Base;
using Microsoft.Extensions.Options;

namespace HPD.Base.InMemory;

public sealed class InMemoryFileStorageProvider : IFileStorageProvider
{
    private readonly ConcurrentDictionary<string, InMemoryFileObject> _objects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _keyIndex = new(StringComparer.Ordinal);
    private readonly long _maxBufferedObjectBytes;
    private readonly TimeProvider _timeProvider;
    private long _nextId;
    private long _nextRevision;

    public InMemoryFileStorageProvider(IOptions<HPDBaseFilesInMemoryOptions> options)
    {
        ProviderRef = options.Value.ProviderRef;
        _maxBufferedObjectBytes = options.Value.MaxBufferedObjectBytes;
        _timeProvider = options.Value.TimeProvider ?? TimeProvider.System;
    }

    public FileProviderRef ProviderRef { get; }

    public ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(
        FileBucketDescriptor bucket,
        FileObjectUploadRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseFilesInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesProviderUpload,
            ProviderOperationValues.Upload,
            request.SizeBytes,
            request.Overwrite,
            () => UploadCoreAsync(bucket, request, context, cancellationToken));

    private async ValueTask<OperationResult<FileObjectUploadResult>> UploadCoreAsync(
        FileBucketDescriptor bucket,
        FileObjectUploadRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Content is null)
            return Validation<FileObjectUploadResult>("content", "Upload content stream is required.");
        if (request.Key is null)
            return Validation<FileObjectUploadResult>("key", "Object key is required.");

        var keyIndex = KeyIndex(bucket.BucketId, request.Key.Value);
        if (_keyIndex.TryGetValue(keyIndex, out var existingId) && !request.Overwrite)
            return OperationResults.Conflict<FileObjectUploadResult>(Error("files.object.exists", "An object with this key already exists.", ErrorCategory.Conflict, "key"));

        var maxBytes = MaxAllowedBytes(bucket);
        var buffered = await ReadBoundedAsync(request.Content, maxBytes, cancellationToken).ConfigureAwait(false);
        if (buffered.Exceeded)
            return OperationResults.ValidationFailed<FileObjectUploadResult>(Error("hpd.base.files.sizeExceeded", "Object size exceeds the configured limit.", ErrorCategory.Validation, "content"));
        if (!ChecksumMatches(request.Checksum, buffered.Content))
            return OperationResults.ValidationFailed<FileObjectUploadResult>(Error("hpd.base.files.checksumRejected", "Checksum does not match uploaded content.", ErrorCategory.Validation, "checksum"));

        var now = _timeProvider.GetUtcNow();
        var objectId = existingId is not null && request.Overwrite ? new FileObjectId(existingId) : new FileObjectId("mem_" + Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var createdAt = _objects.TryGetValue(ObjectIndex(bucket.BucketId, objectId), out var existing) ? existing.CreatedAt : now;
        var stored = new InMemoryFileObject
        {
            BucketId = bucket.BucketId,
            ObjectId = objectId,
            Key = request.Key.Value,
            Name = request.Name,
            ContentType = request.ContentType,
            Content = buffered.Content,
            Checksum = request.Checksum,
            Revision = new FileObjectRevision("rev_" + Interlocked.Increment(ref _nextRevision).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            CreatedAt = createdAt,
            UpdatedAt = now,
            OwnerSubjectId = context.SubjectId,
            TenantId = context.TenantId,
            PublicMetadata = request.Metadata is null ? null : new Dictionary<string, string>(request.Metadata, StringComparer.Ordinal)
        };

        _objects[ObjectIndex(bucket.BucketId, objectId)] = stored;
        _keyIndex[keyIndex] = objectId.Value;

        return OperationResults.Created(new FileObjectUploadResult
        {
            Metadata = stored.ToMetadata(),
            Created = existing is null
        });
    }

    public ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(
        FileBucketDescriptor bucket,
        FileObjectDownloadRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseFilesInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesProviderDownloadOpen,
            ProviderOperationValues.DownloadOpen,
            sizeBytes: null,
            overwriteRequested: null,
            () => OpenDownloadCoreAsync(bucket, request, context, cancellationToken));

    private ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadCoreAsync(
        FileBucketDescriptor bucket,
        FileObjectDownloadRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGet(bucket.BucketId, request.ObjectId, out var stored))
            return ValueTask.FromResult(NotFound<FileObjectDownloadResult>(request.ObjectId));

        var stream = new MemoryStream(stored.Content, writable: false);
        return ValueTask.FromResult(OperationResults.Ok(new FileObjectDownloadResult
        {
            Metadata = stored.ToMetadata(),
            Content = stream,
            ContentLength = stored.Content.LongLength,
            ContentType = stored.ContentType,
            ETag = stored.Revision.Value,
            Revision = stored.Revision,
            OwnsStream = true
        }));
    }

    public ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(
        FileBucketDescriptor bucket,
        FileObjectMetadataRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseFilesInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesProviderMetadataGet,
            ProviderOperationValues.MetadataGet,
            sizeBytes: null,
            overwriteRequested: null,
            () => GetMetadataCoreAsync(bucket, request, context, cancellationToken));

    private ValueTask<OperationResult<FileObjectMetadata>> GetMetadataCoreAsync(
        FileBucketDescriptor bucket,
        FileObjectMetadataRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(TryGet(bucket.BucketId, request.ObjectId, out var stored)
            ? OperationResults.Ok(stored.ToMetadata())
            : NotFound<FileObjectMetadata>(request.ObjectId));
    }

    public ValueTask<OperationResult> DeleteAsync(
        FileBucketDescriptor bucket,
        FileObjectDeleteRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseFilesInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesProviderDelete,
            ProviderOperationValues.Delete,
            sizeBytes: null,
            overwriteRequested: null,
            () => DeleteCoreAsync(bucket, request, context, cancellationToken));

    private ValueTask<OperationResult> DeleteCoreAsync(
        FileBucketDescriptor bucket,
        FileObjectDeleteRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_objects.TryRemove(ObjectIndex(bucket.BucketId, request.ObjectId), out var stored))
            return ValueTask.FromResult(new OperationResult
            {
                Status = OperationStatus.NotFound,
                Error = Error("files.object.notFound", "File object was not found.", ErrorCategory.NotFound, request.ObjectId.Value)
            });

        _keyIndex.TryRemove(KeyIndex(bucket.BucketId, stored.Key), out _);
        return ValueTask.FromResult(OperationResults.NoContent());
    }

    public ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(
        FileBucketDescriptor bucket,
        FileObjectListRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseFilesInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.FilesProviderList,
            ProviderOperationValues.List,
            sizeBytes: null,
            overwriteRequested: null,
            () => ListMetadataCoreAsync(bucket, request, context, cancellationToken));

    private ValueTask<OperationResult<FileObjectListResult>> ListMetadataCoreAsync(
        FileBucketDescriptor bucket,
        FileObjectListRequest request,
        FileOperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = _objects.Values
            .Where(item => item.BucketId == bucket.BucketId)
            .Where(item => request.Prefix is null || item.Key.Value.StartsWith(request.Prefix.Value.Value, StringComparison.Ordinal))
            .OrderBy(static item => item.Key.Value, StringComparer.Ordinal)
            .Take(Math.Clamp(request.Limit ?? 100, 0, 500))
            .Select(static item => item.ToMetadata())
            .ToArray();

        return ValueTask.FromResult(OperationResults.Ok(new FileObjectListResult { Items = items }));
    }

    public void Clear()
    {
        _objects.Clear();
        _keyIndex.Clear();
    }

    private bool TryGet(FileBucketId bucketId, FileObjectId objectId, out InMemoryFileObject stored) =>
        _objects.TryGetValue(ObjectIndex(bucketId, objectId), out stored!);

    private static OperationResult<T> Validation<T>(string target, string message) =>
        OperationResults.ValidationFailed<T>(Error("files.inmemory.validation", message, ErrorCategory.Validation, target));

    private static OperationResult<T> NotFound<T>(FileObjectId objectId) =>
        OperationResults.NotFound<T>(Error("files.object.notFound", "File object was not found.", ErrorCategory.NotFound, objectId.Value));

    private static BaseError Error(string code, string message, ErrorCategory category, string? target) => new()
    {
        Code = code,
        Message = message,
        Category = category,
        Target = target
    };

    private static string ObjectIndex(FileBucketId bucketId, FileObjectId objectId) => bucketId.Value + "\n" + objectId.Value;
    private static string KeyIndex(FileBucketId bucketId, FileObjectKey key) => bucketId.Value + "\n" + key.Value;

    private long MaxAllowedBytes(FileBucketDescriptor bucket) =>
        bucket.MaxObjectBytes is null ? _maxBufferedObjectBytes : Math.Min(bucket.MaxObjectBytes.Value, _maxBufferedObjectBytes);

    private static async ValueTask<BufferedUpload> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes < 0)
            return new BufferedUpload([], true);

        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return new BufferedUpload(buffer.ToArray(), false);
            if (buffer.Length + read > maxBytes)
                return new BufferedUpload([], true);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ChecksumMatches(FileObjectChecksum? checksum, byte[] content)
    {
        if (checksum is null)
            return true;

        const string prefix = "sha256:";
        var value = checksum.Value.Value;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        var expected = value[prefix.Length..];
        var actual = Convert.ToHexString(SHA256.HashData(content));
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct BufferedUpload(byte[] Content, bool Exceeded);
}

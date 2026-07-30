using HPD.Base.Application.Results;
using HPD.Base.Files.Objects;

namespace HPD.Base.Application.Files;

/// <summary>Opens bucket-bound file handles under one session identity.</summary>
public sealed class BaseSessionFiles(
    IFileObjectService service,
    FileOperationContext context)
{
    public BaseFileBucket Bucket(string bucketId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketId);
        return new BaseFileBucket(service, context, new FileBucketId(bucketId));
    }
}

/// <summary>Performs file operations without repeating bucket or identity context.</summary>
public sealed class BaseFileBucket(
    IFileObjectService service,
    FileOperationContext context,
    FileBucketId bucketId)
{
    public FileBucketId Id => bucketId;

    public async ValueTask<BaseResult<FileObjectUploadResult>> UploadAsync(
        string key,
        Stream content,
        string? contentType = null,
        bool overwrite = false,
        FileObjectChecksum? checksum = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);
        var result = await service.UploadAsync(
            new FileObjectUploadRequest
            {
                BucketId = bucketId,
                Key = new FileObjectKey(key),
                Content = content,
                ContentType = contentType,
                Overwrite = overwrite,
                Checksum = checksum,
                Metadata = metadata is null
                    ? null
                    : new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            },
            context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static value => value);
    }

    public async ValueTask<BaseResult<FileObjectDownloadResult>> OpenReadAsync(
        FileObjectId objectId,
        CancellationToken cancellationToken = default)
    {
        var result = await service.OpenDownloadAsync(
            new FileObjectDownloadRequest { BucketId = bucketId, ObjectId = objectId },
            context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static value => value);
    }

    public async ValueTask<BaseResult<FileObjectMetadata>> GetMetadataAsync(
        FileObjectId objectId,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetMetadataAsync(
            new FileObjectMetadataRequest { BucketId = bucketId, ObjectId = objectId },
            context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static value => value);
    }

    public async ValueTask<BaseResult<BaseUnit>> DeleteAsync(
        FileObjectId objectId,
        CancellationToken cancellationToken = default)
    {
        var result = await service.DeleteAsync(
            new FileObjectDeleteRequest { BucketId = bucketId, ObjectId = objectId },
            context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result);
    }

    public async ValueTask<BaseResult<FileObjectListResult>> ListAsync(
        string? prefix,
        int maximum,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        var result = await service.ListMetadataAsync(
            new FileObjectListRequest
            {
                BucketId = bucketId,
                Prefix = string.IsNullOrEmpty(prefix) ? null : new FileObjectKey(prefix),
                Limit = maximum,
                Cursor = cursor,
            },
            context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static value => value);
    }
}

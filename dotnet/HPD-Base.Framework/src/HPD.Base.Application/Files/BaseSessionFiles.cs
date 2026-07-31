using HPD.Base.Application.Results;
using HPD.Base.Files.Objects;

namespace HPD.Base.Application.Files;

/// <summary>Opens bucket-bound file handles under one session identity.</summary>
public sealed class BaseSessionFiles
{
    private readonly IFileObjectService _service;
    private readonly FileOperationContext _context;

    internal BaseSessionFiles(
        IFileObjectService service,
        FileOperationContext context)
    {
        _service = service;
        _context = context;
    }

    public BaseFileBucket Bucket(string bucketId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketId);
        return new BaseFileBucket(_service, _context, new FileBucketId(bucketId));
    }
}

/// <summary>Performs file operations without repeating bucket or identity context.</summary>
public sealed class BaseFileBucket
{
    private readonly IFileObjectService _service;
    private readonly FileOperationContext _context;

    internal BaseFileBucket(
        IFileObjectService service,
        FileOperationContext context,
        FileBucketId bucketId)
    {
        _service = service;
        _context = context;
        Id = bucketId;
    }

    public FileBucketId Id { get; }

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
        var result = await _service.UploadAsync(
            new FileObjectUploadRequest
            {
                BucketId = Id,
                Key = new FileObjectKey(key),
                Content = content,
                ContentType = contentType,
                SizeBytes = content.CanSeek
                    ? content.Length - content.Position
                    : null,
                Overwrite = overwrite,
                Checksum = checksum,
                Metadata = metadata is null
                    ? null
                    : new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            },
            _context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static value => value);
    }

    public async ValueTask<BaseResult<FileObjectDownloadResult>> OpenReadAsync(
        FileObjectId objectId,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.OpenDownloadAsync(
            new FileObjectDownloadRequest { BucketId = Id, ObjectId = objectId },
            _context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static value => value);
    }

    public async ValueTask<BaseResult<FileObjectMetadata>> GetMetadataAsync(
        FileObjectId objectId,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetMetadataAsync(
            new FileObjectMetadataRequest { BucketId = Id, ObjectId = objectId },
            _context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static value => value);
    }

    public async ValueTask<BaseResult<BaseUnit>> DeleteAsync(
        FileObjectId objectId,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.DeleteAsync(
            new FileObjectDeleteRequest { BucketId = Id, ObjectId = objectId },
            _context,
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
        var result = await _service.ListMetadataAsync(
            new FileObjectListRequest
            {
                BucketId = Id,
                Prefix = string.IsNullOrEmpty(prefix) ? null : new FileObjectKey(prefix),
                Limit = maximum,
                Cursor = cursor,
            },
            _context,
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static value => value);
    }
}

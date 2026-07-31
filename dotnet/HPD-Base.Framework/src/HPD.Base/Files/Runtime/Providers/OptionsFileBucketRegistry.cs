using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class OptionsFileBucketRegistry : IFileBucketRegistry
{
    private readonly HPDBaseFilesOptions _options;

    public OptionsFileBucketRegistry(IOptions<HPDBaseFilesOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<FileBucketDescriptor?> FindAsync(FileBucketId bucketId, CancellationToken cancellationToken = default)
    {
        var bucket = _options.Buckets.FirstOrDefault(candidate => string.Equals(candidate.BucketId.Value, bucketId.Value, StringComparison.Ordinal));
        return ValueTask.FromResult(bucket);
    }

    public ValueTask<FileBucketDescriptor[]> ListAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_options.Buckets.ToArray());
}

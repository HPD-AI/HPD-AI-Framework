using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class OptionsFileBucketRegistry : IFileBucketRegistry
{
    private readonly HPDBaseFilesOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public OptionsFileBucketRegistry(IOptions<HPDBaseFilesOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Executes the find async operation.</summary>
    public ValueTask<FileBucketDescriptor?> FindAsync(FileBucketId bucketId, CancellationToken cancellationToken = default)
    {
        var bucket = _options.Buckets.FirstOrDefault(candidate => string.Equals(candidate.BucketId.Value, bucketId.Value, StringComparison.Ordinal));
        return ValueTask.FromResult(bucket);
    }

    /// <summary>Executes the list async operation.</summary>
    public ValueTask<FileBucketDescriptor[]> ListAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_options.Buckets.ToArray());
}

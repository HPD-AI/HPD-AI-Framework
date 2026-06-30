using HPD.Base.Files.Buckets;
using HPD.Base.Files.Providers;

namespace HPD.Base.Files.Providers;

internal sealed class DefaultFileStorageProviderResolver : IFileStorageProviderResolver
{
    private readonly IEnumerable<IFileStorageProvider> _providers;

    public DefaultFileStorageProviderResolver(IEnumerable<IFileStorageProvider> providers)
    {
        _providers = providers;
    }

    public ValueTask<IFileStorageProvider?> ResolveAsync(FileBucketDescriptor bucket, CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(candidate => string.Equals(candidate.ProviderRef.Value, bucket.ProviderRef?.Value, StringComparison.Ordinal));
        return ValueTask.FromResult(provider);
    }
}

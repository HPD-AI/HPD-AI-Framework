
namespace HPD.Base;

internal sealed class DefaultFileStorageProviderResolver : IFileStorageProviderResolver
{
    private readonly IEnumerable<IFileStorageProvider> _providers;

    /// <summary>Initializes a new instance.</summary>
    public DefaultFileStorageProviderResolver(IEnumerable<IFileStorageProvider> providers)
    {
        _providers = providers;
    }

    /// <summary>Executes the resolve async operation.</summary>
    public ValueTask<IFileStorageProvider?> ResolveAsync(FileBucketDescriptor bucket, CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(candidate => string.Equals(candidate.ProviderRef.Value, bucket.ProviderRef?.Value, StringComparison.Ordinal));
        return ValueTask.FromResult(provider);
    }
}


namespace HPD.Base;

public sealed record FileModuleOptionsContract
{
    public bool Enabled { get; init; } = true;
    public long? MaxObjectBytes { get; init; }
    public int? MaxKeyLength { get; init; }
    public int? MaxKeySegments { get; init; }
    public FileBucketRegistration[]? Buckets { get; init; }
}

public sealed record FileBucketRegistration
{
    public required FileBucketId BucketId { get; init; }
    public FileProviderRef? ProviderRef { get; init; }
    public FileBucketDescriptor? Descriptor { get; init; }
}

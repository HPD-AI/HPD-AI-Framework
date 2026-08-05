
namespace HPD.Base;

/// <summary>Represents a file module options contract.</summary>
public sealed record FileModuleOptionsContract
{
    /// <summary>Gets or sets the enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets or sets the max object bytes.</summary>
    public long? MaxObjectBytes { get; init; }
    /// <summary>Gets or sets the max key length.</summary>
    public int? MaxKeyLength { get; init; }
    /// <summary>Gets or sets the max key segments.</summary>
    public int? MaxKeySegments { get; init; }
    /// <summary>Gets or sets the buckets.</summary>
    public FileBucketRegistration[]? Buckets { get; init; }
}

/// <summary>Represents a file bucket registration.</summary>
public sealed record FileBucketRegistration
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the provider ref.</summary>
    public FileProviderRef? ProviderRef { get; init; }
    /// <summary>Gets or sets the descriptor.</summary>
    public FileBucketDescriptor? Descriptor { get; init; }
}


namespace HPD.Base;

/// <summary>Represents a hpdbase files options.</summary>
public sealed class HPDBaseFilesOptions
{
    /// <summary>Gets or sets the enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets the max object bytes.</summary>
    public long? MaxObjectBytes { get; set; } = 104_857_600;
    /// <summary>Gets or sets the max key length.</summary>
    public int MaxKeyLength { get; set; } = 1024;
    /// <summary>Gets or sets the max key segments.</summary>
    public int MaxKeySegments { get; set; } = 32;
    /// <summary>Gets the buckets.</summary>
    public List<FileBucketDescriptor> Buckets { get; } = [];
}

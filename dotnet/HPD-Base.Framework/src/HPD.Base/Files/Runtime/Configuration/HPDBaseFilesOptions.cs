
namespace HPD.Base;

public sealed class HPDBaseFilesOptions
{
    public bool Enabled { get; set; } = true;
    public long? MaxObjectBytes { get; set; } = 104_857_600;
    public int MaxKeyLength { get; set; } = 1024;
    public int MaxKeySegments { get; set; } = 32;
    public List<FileBucketDescriptor> Buckets { get; } = [];
}

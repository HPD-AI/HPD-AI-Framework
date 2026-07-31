namespace HPD.Base;

public sealed class HPDBaseRuntimeLimitOptions
{
    public int MaxFilterDepth { get; set; } = 8;
    public int MaxFilterNodes { get; set; } = 128;
    public int MaxSerializedQueryLength { get; set; } = 64 * 1024;
    public int MaxInValues { get; set; } = 256;
    public int MaxExtensionArguments { get; set; } = 16;
    public int MaxIncludeDepth { get; set; } = 3;
    public int MaxIncludeCount { get; set; } = 8;
    public int MaxSortFields { get; set; } = 8;
    public int MaxSelectFields { get; set; } = 128;
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 500;
}

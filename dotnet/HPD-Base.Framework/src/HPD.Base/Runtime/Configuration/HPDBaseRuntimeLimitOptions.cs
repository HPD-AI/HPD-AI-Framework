namespace HPD.Base;

/// <summary>Represents a hpdbase runtime limit options.</summary>
public sealed class HPDBaseRuntimeLimitOptions
{
    /// <summary>Gets or sets the max filter depth.</summary>
    public int MaxFilterDepth { get; set; } = 8;
    /// <summary>Gets or sets the max filter nodes.</summary>
    public int MaxFilterNodes { get; set; } = 128;
    /// <summary>Gets or sets the max serialized query length.</summary>
    public int MaxSerializedQueryLength { get; set; } = 64 * 1024;
    /// <summary>Gets or sets the max in values.</summary>
    public int MaxInValues { get; set; } = 256;
    /// <summary>Gets or sets the max extension arguments.</summary>
    public int MaxExtensionArguments { get; set; } = 16;
    /// <summary>Gets or sets the max include depth.</summary>
    public int MaxIncludeDepth { get; set; } = 3;
    /// <summary>Gets or sets the max include count.</summary>
    public int MaxIncludeCount { get; set; } = 8;
    /// <summary>Gets or sets the max sort fields.</summary>
    public int MaxSortFields { get; set; } = 8;
    /// <summary>Gets or sets the max select fields.</summary>
    public int MaxSelectFields { get; set; } = 128;
    /// <summary>Gets or sets the default page size.</summary>
    public int DefaultPageSize { get; set; } = 50;
    /// <summary>Gets or sets the max page size.</summary>
    public int MaxPageSize { get; set; } = 500;
}

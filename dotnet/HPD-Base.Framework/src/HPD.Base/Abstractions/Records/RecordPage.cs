
namespace HPD.Base;

/// <summary>Represents a record page.</summary>
public sealed record RecordPage
{
    /// <summary>Gets or sets the items.</summary>
    public required RecordEnvelope[] Items { get; init; }
    /// <summary>Gets or sets the page.</summary>
    public required PageInfo Page { get; init; }
    /// <summary>Gets or sets the count.</summary>
    public CountInfo? Count { get; init; }
}

/// <summary>Represents a page info.</summary>
public sealed record PageInfo
{
    /// <summary>Gets or sets the page.</summary>
    public int? Page { get; init; }
    /// <summary>Gets or sets the per page.</summary>
    public int? PerPage { get; init; }
    /// <summary>Gets or sets the offset.</summary>
    public int? Offset { get; init; }
    /// <summary>Gets or sets the limit.</summary>
    public int? Limit { get; init; }
    /// <summary>Gets or sets the cursor.</summary>
    public string? Cursor { get; init; }
    /// <summary>Gets or sets the next cursor.</summary>
    public string? NextCursor { get; init; }
    /// <summary>Gets or sets the has more.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Represents a count info.</summary>
public sealed record CountInfo
{
    /// <summary>Gets or sets the mode.</summary>
    public required QueryCountMode Mode { get; init; }
    /// <summary>Gets or sets the total.</summary>
    public long? Total { get; init; }
    /// <summary>Gets or sets the is exact.</summary>
    public bool IsExact { get; init; }
}

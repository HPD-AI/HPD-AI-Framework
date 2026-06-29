using HPD.Base.Query;

namespace HPD.Base.Records;

public sealed record RecordPage
{
    public required RecordEnvelope[] Items { get; init; }
    public required PageInfo Page { get; init; }
    public CountInfo? Count { get; init; }
    public string? DependencyToken { get; init; }
}

public sealed record PageInfo
{
    public int? Page { get; init; }
    public int? PerPage { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }
    public string? Cursor { get; init; }
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
}

public sealed record CountInfo
{
    public required QueryCountMode Mode { get; init; }
    public long? Total { get; init; }
    public bool IsExact { get; init; }
}

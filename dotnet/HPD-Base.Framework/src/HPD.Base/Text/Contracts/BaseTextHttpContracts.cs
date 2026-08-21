namespace HPD.Base;

/// <summary>Contains one closed lexical query node on the HTTP wire.</summary>
public sealed record BaseTextHttpQueryNode
{
    /// <summary>Gets the closed node kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the term or prefix value.</summary>
    public string? Value { get; init; }
    /// <summary>Gets phrase terms.</summary>
    public string[]? Terms { get; init; }
    /// <summary>Gets the stable field identity.</summary>
    public string? Field { get; init; }
    /// <summary>Gets the unary child.</summary>
    public BaseTextHttpQueryNode? Child { get; init; }
    /// <summary>Gets logical children.</summary>
    public BaseTextHttpQueryNode[]? Children { get; init; }
}
/// <summary>Contains one closed ordinary pre-ranking filter on the HTTP wire.</summary>
public sealed record BaseTextHttpFilter
{
    /// <summary>Gets the closed filter kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the stable filter-field identity.</summary>
    public string? Field { get; init; }
    /// <summary>Gets the scalar value.</summary>
    public BaseTextHttpFilterValue? Value { get; init; }
    /// <summary>Gets the bounded value sequence.</summary>
    public BaseTextHttpFilterValue[]? Values { get; init; }
    /// <summary>Gets logical children.</summary>
    public BaseTextHttpFilter[]? Children { get; init; }
}
/// <summary>Contains one tagged lexical-filter value.</summary>
public sealed record BaseTextHttpFilterValue
{
    /// <summary>Gets the closed value kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets a string or ID value.</summary>
    public string? Text { get; init; }
    /// <summary>Gets a Boolean value.</summary>
    public bool? Boolean { get; init; }
    /// <summary>Gets an integer value.</summary>
    public long? Integer { get; init; }
}
/// <summary>Contains one bounded lexical query request.</summary>
public sealed record BaseTextHttpQueryRequest
{
    /// <summary>Gets the exact index identity.</summary>
    public required string IndexId { get; init; }
    /// <summary>Gets the lexical query.</summary>
    public required BaseTextHttpQueryNode Query { get; init; }
    /// <summary>Gets the optional ordinary filter.</summary>
    public BaseTextHttpFilter? Filter { get; init; }
    /// <summary>Gets the bounded secondary ordering.</summary>
    public required BaseTextHttpOrder[] Order { get; init; }
    /// <summary>Gets the result bound.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the optional opaque cursor.</summary>
    public string? Cursor { get; init; }
    /// <summary>Gets the consistency mode.</summary>
    public required string Consistency { get; init; }
    /// <summary>Gets the optional consistency token.</summary>
    public string? ConsistencyToken { get; init; }
    /// <summary>Gets the bounded-staleness age.</summary>
    public long? MaximumAgeMilliseconds { get; init; }
}
/// <summary>Contains one graph-bound secondary ordering field.</summary>
public sealed record BaseTextHttpOrder
{
    /// <summary>Gets the graph-owned field name.</summary>
    public required string Field { get; init; }
    /// <summary>Gets the closed ascending or descending direction.</summary>
    public required string Direction { get; init; }
    /// <summary>Gets the closed null placement.</summary>
    public required string NullOrder { get; init; }
}
/// <summary>Contains one authoritative lexical match on the HTTP wire.</summary>
public sealed record BaseTextHttpMatch
{
    /// <summary>Gets the authorized record envelope.</summary>
    public required RecordEnvelope Record { get; init; }
    /// <summary>Gets the opaque revision.</summary>
    public required string Revision { get; init; }
    /// <summary>Gets fixed-point score units.</summary>
    public required string ScoreUnits { get; init; }
}
/// <summary>Contains one bounded lexical result page on the HTTP wire.</summary>
public sealed record BaseTextHttpResult
{
    /// <summary>Gets authoritative matches.</summary>
    public required BaseTextHttpMatch[] Matches { get; init; }
    /// <summary>Gets the optional opaque continuation.</summary>
    public string? Next { get; init; }
    /// <summary>Gets the opaque consistency token.</summary>
    public required string ConsistencyToken { get; init; }
}
/// <summary>Contains one bounded lexical result page with a graph-owned record projection.</summary>
public sealed record BaseTextHttpResult<T>
{
    /// <summary>Gets authoritative matches.</summary>
    public required BaseTextHttpMatch<T>[] Matches { get; init; }
    /// <summary>Gets the optional opaque continuation.</summary>
    public string? Next { get; init; }
    /// <summary>Gets the opaque consistency token.</summary>
    public required string ConsistencyToken { get; init; }
}
/// <summary>Contains one lexical match with a graph-owned record projection.</summary>
public sealed record BaseTextHttpMatch<T>
{
    /// <summary>Gets the graph-owned record projection.</summary>
    public required T Record { get; init; }
    /// <summary>Gets the opaque revision.</summary>
    public required string Revision { get; init; }
    /// <summary>Gets fixed-point score units.</summary>
    public required string ScoreUnits { get; init; }
}
/// <summary>Contains a stable lexical HTTP failure.</summary>
public sealed record BaseTextHttpError
{
    /// <summary>Gets the stable failure code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets the fixed safe message.</summary>
    public required string Message { get; init; }
}
/// <summary>Contains one identified text-index rebuild command.</summary>
public sealed record BaseTextHttpRebuildRequest
{
    /// <summary>Gets the expected visible generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the receipt scope.</summary>
    public required string Scope { get; init; }
    /// <summary>Gets the receipt operation.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets the idempotency key.</summary>
    public required string IdempotencyKey { get; init; }
    /// <summary>Gets the Base64 request fingerprint.</summary>
    public required string Fingerprint { get; init; }
}

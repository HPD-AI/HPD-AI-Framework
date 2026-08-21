using System.Text.Json.Serialization;

#pragma warning disable CS1591

namespace HPD.Base;

/// <summary>Contains one closed lexical query node on the HTTP wire.</summary>
public sealed record BaseTextHttpQueryNode
{
    public required string Kind { get; init; }
    public string? Value { get; init; }
    public string[]? Terms { get; init; }
    public string? Field { get; init; }
    public BaseTextHttpQueryNode? Child { get; init; }
    public BaseTextHttpQueryNode[]? Children { get; init; }
}

/// <summary>Contains one closed ordinary pre-ranking filter on the HTTP wire.</summary>
public sealed record BaseTextHttpFilter
{
    public required string Kind { get; init; }
    public string? Field { get; init; }
    public BaseTextHttpFilterValue? Value { get; init; }
    public BaseTextHttpFilterValue[]? Values { get; init; }
    public BaseTextHttpFilter[]? Children { get; init; }
}

/// <summary>Contains one tagged lexical-filter value.</summary>
public sealed record BaseTextHttpFilterValue
{
    public required string Kind { get; init; }
    public string? Text { get; init; }
    public bool? Boolean { get; init; }
    public long? Integer { get; init; }
}

/// <summary>Contains one bounded lexical query request.</summary>
public sealed record BaseTextHttpQueryRequest
{
    public required string IndexId { get; init; }
    public required BaseTextHttpQueryNode Query { get; init; }
    public BaseTextHttpFilter? Filter { get; init; }
    public required int Take { get; init; }
    public string? Cursor { get; init; }
    public required string Consistency { get; init; }
    public string? ConsistencyToken { get; init; }
    public long? MaximumAgeMilliseconds { get; init; }
}

/// <summary>Contains one authoritative lexical match on the HTTP wire.</summary>
public sealed record BaseTextHttpMatch
{
    public required RecordEnvelope Record { get; init; }
    public required string Revision { get; init; }
    public required string ScoreUnits { get; init; }
}

/// <summary>Contains one bounded lexical result page on the HTTP wire.</summary>
public sealed record BaseTextHttpResult
{
    public required BaseTextHttpMatch[] Matches { get; init; }
    public string? Next { get; init; }
    public required string ConsistencyToken { get; init; }
}

/// <summary>Contains a stable lexical HTTP failure.</summary>
public sealed record BaseTextHttpError { public required string Code { get; init; } public required string Message { get; init; } }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BaseTextHttpQueryRequest))]
[JsonSerializable(typeof(BaseTextHttpResult))]
[JsonSerializable(typeof(BaseTextHttpError))]
internal sealed partial class BaseTextHttpJsonContext : JsonSerializerContext;

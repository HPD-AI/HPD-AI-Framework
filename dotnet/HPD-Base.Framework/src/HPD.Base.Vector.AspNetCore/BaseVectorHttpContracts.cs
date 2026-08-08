using System.Text.Json.Serialization;

namespace HPD.Base.Vector.AspNetCore;

/// <summary>Controls whether labeled vector measures are disclosed in an HTTP result.</summary>
public enum BaseVectorHttpMeasureDisclosure
{
    /// <summary>Omits measures while preserving rank and authoritative records.</summary>
    Omit,
    /// <summary>Includes the finite function-labeled measure for each match.</summary>
    Include,
}

/// <summary>Contains one bounded HTTP vector query.</summary>
public sealed record BaseVectorHttpQueryRequest
{
    /// <summary>Gets the externally produced float32 vector.</summary>
    public required float[] Vector { get; init; }
    /// <summary>Gets the required top-K bound.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the explicit closed measure-disclosure mode.</summary>
    public required BaseVectorHttpMeasureDisclosure MeasureDisclosure { get; init; }
    /// <summary>Gets optional equality filters over declared stable field IDs.</summary>
    public BaseVectorHttpFilter[] Filters { get; init; } = [];
    /// <summary>Gets the consistency mode: current, available, or atLeast.</summary>
    public string? Consistency { get; init; }
    /// <summary>Gets the opaque token required by atLeast.</summary>
    public string? ConsistencyToken { get; init; }
}

/// <summary>Contains one typed equality filter.</summary>
public sealed record BaseVectorHttpFilter
{
    /// <summary>Gets the generated stable field identifier.</summary>
    public required string FieldId { get; init; }
    /// <summary>Gets the closed portable value.</summary>
    public required BaseVectorHttpFilterValue Value { get; init; }
}

/// <summary>Contains one closed portable HTTP filter value.</summary>
public sealed record BaseVectorHttpFilterValue
{
    /// <summary>Gets the value kind: null, string, boolean, integer, or id.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the optional string or ID.</summary>
    public string? Text { get; init; }
    /// <summary>Gets the optional Boolean.</summary>
    public bool? Boolean { get; init; }
    /// <summary>Gets the optional integer.</summary>
    public long? Integer { get; init; }
}

/// <summary>Contains one safe HTTP vector match.</summary>
public sealed record BaseVectorHttpMatch
{
    /// <summary>Gets the authoritative redacted record.</summary>
    public required RecordEnvelope Record { get; init; }
    /// <summary>Gets the one-based rank.</summary>
    public required int Rank { get; init; }
    /// <summary>Gets the labeled finite measure when explicitly requested.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BaseVectorMeasure? Measure { get; init; }
}

/// <summary>Contains one complete bounded HTTP vector result.</summary>
public sealed record BaseVectorHttpQueryResponse
{
    /// <summary>Gets the ranked matches.</summary>
    public required BaseVectorHttpMatch[] Matches { get; init; }
    /// <summary>Gets the vector-index identifier.</summary>
    public required string VectorIndexId { get; init; }
    /// <summary>Gets the vector-index generation.</summary>
    public required long VectorIndexGeneration { get; init; }
    /// <summary>Gets the provider identifier.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the accuracy classification.</summary>
    public required BaseVectorResultAccuracy Accuracy { get; init; }
    /// <summary>Gets opaque consistency evidence.</summary>
    public required string ConsistencyToken { get; init; }
}

/// <summary>Contains one bounded HTTP error.</summary>
public sealed record BaseVectorHttpError
{
    /// <summary>Gets the stable error code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets the fixed safe message.</summary>
    public required string Message { get; init; }
}

/// <summary>Contains one bounded vector-index rebuild command.</summary>
public sealed record BaseVectorHttpRebuildRequest
{
    /// <summary>Gets the configured store identifier.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the expected index generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the expected purge generation.</summary>
    public required long ExpectedPurgeGeneration { get; init; }
    /// <summary>Gets the exact destructive confirmation phrase.</summary>
    public required string Confirmation { get; init; }
}

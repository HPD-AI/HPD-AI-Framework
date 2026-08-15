namespace HPD.Base;

/// <summary>
/// Source-generation-friendly tagged value used by portable query contracts.
/// </summary>
public sealed record QueryValue
{
    /// <summary>Gets or sets the kind.</summary>
    public required QueryValueKind Kind { get; init; }
    /// <summary>Gets or sets the string.</summary>
    public string? String { get; init; }
    /// <summary>Gets or sets the boolean.</summary>
    public bool? Boolean { get; init; }
    /// <summary>Gets or sets the integer.</summary>
    public long? Integer { get; init; }
    /// <summary>Gets or sets the number.</summary>
    public double? Number { get; init; }
    /// <summary>Gets or sets the decimal.</summary>
    public string? Decimal { get; init; }
    /// <summary>Gets or sets the date time.</summary>
    public DateTimeOffset? DateTime { get; init; }
    /// <summary>Gets or sets the ID.</summary>
    public string? Id { get; init; }
    /// <summary>Gets or sets the array.</summary>
    public QueryValue[]? Array { get; init; }
    /// <summary>Gets the canonical exported-subject identifier for a subject-reference value.</summary>
    public string? SubjectId { get; init; }
    /// <summary>Gets the canonical subject-identifier grammar for a subject-reference value.</summary>
    public BaseSubjectIdKind? SubjectIdKind { get; init; }
    /// <summary>Gets the installed maximum canonical subject-identifier byte count.</summary>
    public int? SubjectIdMaximumUtf8Bytes { get; init; }
    /// <summary>Gets the canonical unpadded base64url authority epoch for a subject-reference value.</summary>
    public string? SubjectAuthorityEpoch { get; init; }
    /// <summary>Gets the canonical unpadded base64url incarnation for a subject-reference value.</summary>
    public string? SubjectIncarnation { get; init; }
}

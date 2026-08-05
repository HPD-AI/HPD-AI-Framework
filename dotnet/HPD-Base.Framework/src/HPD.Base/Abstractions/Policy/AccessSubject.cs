namespace HPD.Base;

/// <summary>Represents a access subject.</summary>
public sealed record AccessSubject
{
    /// <summary>Gets or sets the kind.</summary>
    public required AccessSubjectKind Kind { get; init; }
    /// <summary>Gets or sets the ID.</summary>
    public string? Id { get; init; }
    /// <summary>Gets or sets the qualifier.</summary>
    public string? Qualifier { get; init; }
    /// <summary>Gets or sets the tenant ID.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets or sets the source.</summary>
    public string? Source { get; init; }
}

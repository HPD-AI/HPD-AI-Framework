namespace HPD.Base;

/// <summary>Describes the semantic kind of a dependency template.</summary>
public enum BaseDependencyKind
{
    /// <summary>Identifies collection.</summary>
Collection,
    /// <summary>Identifies record.</summary>
Record,
    /// <summary>Identifies named.</summary>
Named,
    /// <summary>Identifies auth context.</summary>
AuthContext,
    /// <summary>Identifies operation result.</summary>
OperationResult,
    /// <summary>Identifies external.</summary>
External
}

/// <summary>Controls where dependency-template metadata may be advertised.</summary>
public enum BaseDependencyVisibility
{
    /// <summary>Identifies public.</summary>
Public,
    /// <summary>Identifies admin.</summary>
Admin,
    /// <summary>Identifies internal.</summary>
Internal
}

/// <summary>Describes a bounded public dependency shape without resolved values.</summary>
public sealed record BaseDependencyTemplate
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required BaseDependencyKind Kind { get; init; }
    /// <summary>Gets or sets the parameter names.</summary>
    public string[] ParameterNames { get; init; } = [];
    /// <summary>Gets or sets the visibility.</summary>
    public BaseDependencyVisibility Visibility { get; init; }
    /// <summary>Gets or sets the description.</summary>
    public string? Description { get; init; }
}

/// <summary>Identifies one resolved dependency without exposing its parameter values.</summary>
public sealed record BaseDependencyReference
{
    /// <summary>Gets or sets the template ID.</summary>
    public required string TemplateId { get; init; }
    /// <summary>Gets or sets the value.</summary>
    public required string Value { get; init; }
}

/// <summary>Contains the deduplicated dependencies of one result.</summary>
public sealed record BaseDependencySet
{
    /// <summary>Gets or sets the references.</summary>
    public required BaseDependencyReference[] References { get; init; }
}

/// <summary>Signals that matching dependency sets may now be stale.</summary>
public sealed record BaseDependencyInvalidation
{
    /// <summary>Gets or sets the event ID.</summary>
    public required string EventId { get; init; }
    /// <summary>Gets or sets the occurred at.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
    /// <summary>Gets or sets the reason.</summary>
    public required string Reason { get; init; }
    /// <summary>Gets or sets the references.</summary>
    public required BaseDependencyReference[] References { get; init; }
}

/// <summary>Stable built-in dependency identifiers.</summary>
public static class BaseDependencyIds
{
    /// <summary>Provides the collection value.</summary>
    public const string Collection = "base.collection";
    /// <summary>Provides the record value.</summary>
    public const string Record = "base.record";
    /// <summary>Provides the protected exported-subject contract-generation dependency.</summary>
    public const string SubjectContract = "base.subject.contract";
}

/// <summary>Stable bounded invalidation reasons.</summary>
public static class BaseDependencyInvalidationReasons
{
    /// <summary>Provides the record mutation value.</summary>
    public const string RecordMutation = "recordMutation";
    /// <summary>Provides the exported-subject authority publication value.</summary>
    public const string SubjectAuthorityChanged = "subjectAuthorityChanged";
}

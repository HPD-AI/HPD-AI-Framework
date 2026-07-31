namespace HPD.Base;

/// <summary>Describes the semantic kind of a dependency template.</summary>
public enum BaseDependencyKind
{
    Collection,
    Record,
    Named,
    AuthContext,
    OperationResult,
    External
}

/// <summary>Controls where dependency-template metadata may be advertised.</summary>
public enum BaseDependencyVisibility
{
    Public,
    Admin,
    Internal
}

/// <summary>Describes a bounded public dependency shape without resolved values.</summary>
public sealed record BaseDependencyTemplate
{
    public required string Id { get; init; }
    public required BaseDependencyKind Kind { get; init; }
    public string[] ParameterNames { get; init; } = [];
    public BaseDependencyVisibility Visibility { get; init; }
    public string? Description { get; init; }
}

/// <summary>Identifies one resolved dependency without exposing its parameter values.</summary>
public sealed record BaseDependencyReference
{
    public required string TemplateId { get; init; }
    public required string Value { get; init; }
}

/// <summary>Contains the deduplicated dependencies of one result.</summary>
public sealed record BaseDependencySet
{
    public required BaseDependencyReference[] References { get; init; }
}

/// <summary>Signals that matching dependency sets may now be stale.</summary>
public sealed record BaseDependencyInvalidation
{
    public required string EventId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Reason { get; init; }
    public required BaseDependencyReference[] References { get; init; }
}

/// <summary>Stable built-in dependency identifiers.</summary>
public static class BaseDependencyIds
{
    public const string Collection = "base.collection";
    public const string Record = "base.record";
}

/// <summary>Stable bounded invalidation reasons.</summary>
public static class BaseDependencyInvalidationReasons
{
    public const string RecordMutation = "recordMutation";
}

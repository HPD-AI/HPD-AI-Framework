
namespace HPD.Base;

public sealed record AccessGrant
{
    public required string Id { get; init; }
    public required AccessSubject Subject { get; init; }
    public required string Action { get; init; }
    public required ResourceScope Scope { get; init; }
    public GrantEffect Effect { get; init; } = GrantEffect.Allow;

    /// <summary>
    /// Gets the read-side record filter constraint for this grant.
    /// </summary>
    public FilterExpression? Condition { get; init; }

    /// <summary>
    /// Gets the write-side predicate constraint for this grant.
    /// The runtime evaluates this against the proposed record payload for create,
    /// patch, and replace operations, and against the existing payload for delete.
    /// </summary>
    public FilterExpression? WriteCondition { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Source { get; init; }
}

public sealed record ResourceScope
{
    public required ResourceScopeKind Kind { get; init; }
    public string? CollectionId { get; init; }
    public string? RecordId { get; init; }
    public string? FieldPath { get; init; }
    public string? TenantId { get; init; }
}

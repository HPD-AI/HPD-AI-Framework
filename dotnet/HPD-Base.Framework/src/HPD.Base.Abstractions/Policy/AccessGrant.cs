using HPD.Base.Query;

namespace HPD.Base.Policy;

public sealed record AccessGrant
{
    public required string Id { get; init; }
    public required AccessSubject Subject { get; init; }
    public required string Action { get; init; }
    public required ResourceScope Scope { get; init; }
    public GrantEffect Effect { get; init; } = GrantEffect.Allow;
    public FilterExpression? Condition { get; init; }
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

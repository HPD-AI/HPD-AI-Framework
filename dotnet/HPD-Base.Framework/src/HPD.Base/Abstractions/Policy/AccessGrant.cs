
namespace HPD.Base;

/// <summary>Represents a access grant.</summary>
public sealed record AccessGrant
{
    /// <summary>Gets the exact application identity for a system-access grant.</summary>
    public string? ApplicationId { get; init; }
    /// <summary>Gets the exact installed module or service identity for a system-access grant.</summary>
    public string? ModuleId { get; init; }
    /// <summary>Gets the exact audience for a system-access grant.</summary>
    public HPDBaseEndpointAudience? Audience { get; init; }
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the subject.</summary>
    public required AccessSubject Subject { get; init; }
    /// <summary>Gets or sets the action.</summary>
    public required string Action { get; init; }
    /// <summary>Gets or sets the scope.</summary>
    public required ResourceScope Scope { get; init; }
    /// <summary>Gets or sets the effect.</summary>
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

    /// <summary>Gets or sets the expires at.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Gets or sets the source.</summary>
    public string? Source { get; init; }
}

/// <summary>Represents a resource scope.</summary>
public sealed record ResourceScope
{
    /// <summary>Gets or sets the kind.</summary>
    public required ResourceScopeKind Kind { get; init; }
    /// <summary>Gets or sets the collection ID.</summary>
    public string? CollectionId { get; init; }
    /// <summary>Gets or sets the record ID.</summary>
    public string? RecordId { get; init; }
    /// <summary>Gets or sets the field path.</summary>
    public string? FieldPath { get; init; }
    /// <summary>Gets the optional stable vector-index identifier.</summary>
    public string? VectorIndexId { get; init; }
    /// <summary>Gets the optional stable lexical-index identifier.</summary>
    public string? TextIndexId { get; init; }
    /// <summary>Gets the exact exported logical-subject contract identifier.</summary>
    public string? SubjectContractId { get; init; }
    /// <summary>Gets the exact exported logical-subject contract version.</summary>
    public int? SubjectContractVersion { get; init; }
    /// <summary>Gets or sets the tenant ID.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets the exact project identity for a project-bound grant.</summary>
    public string? ProjectId { get; init; }
}

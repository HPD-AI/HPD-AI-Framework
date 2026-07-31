namespace HPD.Base.Auth;

/// <summary>
/// Describes simple collection authorization rules derived from HPD.Auth principal state.
/// </summary>
public sealed record HPDAuthBaseCollectionRule
{
    /// <summary>
    /// Gets the collection id this rule applies to.
    /// </summary>
    public required string CollectionId { get; init; }

    /// <summary>
    /// Gets the roles allowed to read records in the collection.
    /// </summary>
    public string[]? ReadRoles { get; init; }

    /// <summary>
    /// Gets the roles allowed to write records in the collection.
    /// </summary>
    public string[]? WriteRoles { get; init; }

    /// <summary>
    /// Gets whether anonymous callers may read records in the collection.
    /// </summary>
    public bool AllowAnonymousRead { get; init; }

    /// <summary>
    /// Gets whether authenticated callers may read records in the collection without a role match.
    /// </summary>
    public bool AllowAuthenticatedRead { get; init; }

    /// <summary>
    /// Gets whether the adapter should add a tenant filter for read operations.
    /// </summary>
    public bool RequireTenantMatch { get; init; } = true;

    /// <summary>
    /// Gets the field path that stores the tenant id for tenant-scoped records.
    /// </summary>
    public string? TenantFieldPath { get; init; }

    /// <summary>
    /// Gets the readable field include list.
    /// </summary>
    public string[]? ReadIncludeFields { get; init; }

    /// <summary>
    /// Gets the readable field exclusion list.
    /// </summary>
    public string[]? ReadExcludeFields { get; init; }

    /// <summary>
    /// Gets the writable field include list.
    /// </summary>
    public string[]? WriteIncludeFields { get; init; }

    /// <summary>
    /// Gets the writable field exclusion list.
    /// </summary>
    public string[]? WriteExcludeFields { get; init; }
}

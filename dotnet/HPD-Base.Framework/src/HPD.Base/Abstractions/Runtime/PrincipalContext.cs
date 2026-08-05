
namespace HPD.Base;

/// <summary>Represents a principal context.</summary>
public sealed record PrincipalContext
{
    /// <summary>Gets or sets the authentication state.</summary>
    public required PrincipalAuthenticationState AuthenticationState { get; init; }
    /// <summary>Gets or sets the subject ID.</summary>
    public string? SubjectId { get; init; }
    /// <summary>Gets or sets the subject kind.</summary>
    public AccessSubjectKind SubjectKind { get; init; } = AccessSubjectKind.Anonymous;
    /// <summary>Gets or sets the display name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Gets or sets the claims.</summary>
    public ClaimValue[]? Claims { get; init; }
    /// <summary>Gets or sets the roles.</summary>
    public string[]? Roles { get; init; }
    /// <summary>Gets or sets the subjects.</summary>
    public AccessSubject[]? Subjects { get; init; }
    /// <summary>Gets or sets the tenant memberships.</summary>
    public TenantMembership[]? TenantMemberships { get; init; }
    /// <summary>Gets or sets the current tenant ID.</summary>
    public string? CurrentTenantId { get; init; }
    /// <summary>Gets or sets the session ID.</summary>
    public string? SessionId { get; init; }
    /// <summary>Gets or sets the credential ID.</summary>
    public string? CredentialId { get; init; }
    /// <summary>Gets or sets the auth source.</summary>
    public string? AuthSource { get; init; }
}

/// <summary>Defines the principal authentication state contract.</summary>
public enum PrincipalAuthenticationState { /// <summary>Identifies anonymous.</summary>
Anonymous, /// <summary>Identifies authenticated.</summary>
Authenticated, /// <summary>Identifies service.</summary>
Service, /// <summary>Identifies admin.</summary>
Admin, /// <summary>Identifies system.</summary>
System }

/// <summary>Represents a claim value.</summary>
public sealed record ClaimValue
{
    /// <summary>Gets or sets the type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets or sets the value.</summary>
    public required string Value { get; init; }
    /// <summary>Gets or sets the issuer.</summary>
    public string? Issuer { get; init; }
    /// <summary>Gets or sets the value type.</summary>
    public string? ValueType { get; init; }
}

/// <summary>Represents a tenant membership.</summary>
public sealed record TenantMembership
{
    /// <summary>Gets or sets the tenant ID.</summary>
    public required string TenantId { get; init; }
    /// <summary>Gets or sets the roles.</summary>
    public string[]? Roles { get; init; }
    /// <summary>Gets or sets the source.</summary>
    public string? Source { get; init; }
}

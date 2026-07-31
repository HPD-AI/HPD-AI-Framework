
namespace HPD.Base;

public sealed record PrincipalContext
{
    public required PrincipalAuthenticationState AuthenticationState { get; init; }
    public string? SubjectId { get; init; }
    public AccessSubjectKind SubjectKind { get; init; } = AccessSubjectKind.Anonymous;
    public string? DisplayName { get; init; }
    public ClaimValue[]? Claims { get; init; }
    public string[]? Roles { get; init; }
    public AccessSubject[]? Subjects { get; init; }
    public TenantMembership[]? TenantMemberships { get; init; }
    public string? CurrentTenantId { get; init; }
    public string? SessionId { get; init; }
    public string? CredentialId { get; init; }
    public string? AuthSource { get; init; }
}

public enum PrincipalAuthenticationState { Anonymous, Authenticated, Service, Admin, System }

public sealed record ClaimValue
{
    public required string Type { get; init; }
    public required string Value { get; init; }
    public string? Issuer { get; init; }
    public string? ValueType { get; init; }
}

public sealed record TenantMembership
{
    public required string TenantId { get; init; }
    public string[]? Roles { get; init; }
    public string? Source { get; init; }
}

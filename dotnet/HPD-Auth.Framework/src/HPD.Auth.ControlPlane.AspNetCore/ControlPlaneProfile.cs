namespace HPD.Auth.ControlPlane;

/// <summary>Describes one bounded class of administrative endpoints.</summary>
public sealed class ControlPlaneProfile
{
    public required string Name { get; init; }
    public required string AuthenticationScheme { get; init; }
    public required string AuthenticationProfile { get; init; }
    public required string ActorIdentifierClaim { get; init; }
    public string? TenantClaim { get; init; }
    public string? AuthenticationMethodClaim { get; init; }
    public string? AssuranceClaim { get; init; }
    public string? RateLimitPolicy { get; init; }
    public string? RequestTimeoutPolicy { get; init; }
    public string? OpenApiSecurityScheme { get; init; }
}

/// <summary>Mutable registration builder for a control-plane profile.</summary>
public sealed class ControlPlaneProfileBuilder
{
    public string? AuthenticationScheme { get; set; }
    public string? AuthenticationProfile { get; set; }
    public string? ActorIdentifierClaim { get; set; }
    public string? TenantClaim { get; set; }
    public string? AuthenticationMethodClaim { get; set; }
    public string? AssuranceClaim { get; set; }
    public string? RateLimitPolicy { get; set; }
    public string? RequestTimeoutPolicy { get; set; }
    public string? OpenApiSecurityScheme { get; set; }

    internal ControlPlaneProfile Build(string name) => new()
    {
        Name = name,
        AuthenticationScheme = AuthenticationScheme ?? string.Empty,
        AuthenticationProfile = AuthenticationProfile ?? string.Empty,
        ActorIdentifierClaim = ActorIdentifierClaim ?? string.Empty,
        TenantClaim = TenantClaim,
        AuthenticationMethodClaim = AuthenticationMethodClaim,
        AssuranceClaim = AssuranceClaim,
        RateLimitPolicy = RateLimitPolicy,
        RequestTimeoutPolicy = RequestTimeoutPolicy,
        OpenApiSecurityScheme = OpenApiSecurityScheme
    };
}

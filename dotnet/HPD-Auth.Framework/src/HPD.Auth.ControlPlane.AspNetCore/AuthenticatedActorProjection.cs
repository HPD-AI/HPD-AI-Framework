namespace HPD.Auth.ControlPlane;

/// <summary>Bounded immutable attribution projected from an authorized request.</summary>
public sealed record AuthenticatedActorProjection
{
    public required string ActorId { get; init; }
    public required string AuthenticationProfile { get; init; }
    public string? TenantId { get; init; }
    public string? AuthenticationMethod { get; init; }
    public string? AssuranceLevel { get; init; }
}

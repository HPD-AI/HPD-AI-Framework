namespace HPD.Auth.Admin;

/// <summary>Required security and routing selection for the HPD.Auth Admin API.</summary>
public sealed class HPDAuthAdminEndpointOptions
{
    public string RoutePrefix { get; init; } = "/api/admin";
    public required string ControlPlaneProfile { get; init; }
}

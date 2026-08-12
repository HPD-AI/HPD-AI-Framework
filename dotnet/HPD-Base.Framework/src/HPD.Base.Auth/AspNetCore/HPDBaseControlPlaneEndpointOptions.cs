namespace HPD.Base.Auth;

/// <summary>Configures the official HPD.Auth-secured BASE control-plane endpoint surface.</summary>
public sealed class HPDBaseControlPlaneEndpointOptions
{
    /// <summary>Gets the common route prefix.</summary>
    public required string RoutePrefix { get; init; }
    /// <summary>Gets the HPD.Auth control-plane profile.</summary>
    public required string Profile { get; init; }
    /// <summary>Gets whether records are mapped.</summary>
    public bool MapRecords { get; init; } = true;
    /// <summary>Gets whether registered reads are mapped.</summary>
    public bool MapRegisteredReads { get; init; } = true;
    /// <summary>Gets whether administrative inspection is mapped.</summary>
    public bool MapAdministration { get; init; } = true;
    /// <summary>Gets whether destructive purge and staged backup administration endpoints are mapped.</summary>
    public bool MapArtifactAdministration { get; init; }
    /// <summary>Gets whether policy explanation is mapped.</summary>
    public bool MapPolicyExplain { get; init; } = true;
    /// <summary>Gets whether files are mapped.</summary>
    public bool MapFiles { get; init; }
    /// <summary>Gets whether realtime is mapped.</summary>
    public bool MapRealtime { get; init; }
    /// <summary>Gets whether the authenticated ControlPlane generation snapshot is mapped.</summary>
    public bool MapClientGeneration { get; init; }
}

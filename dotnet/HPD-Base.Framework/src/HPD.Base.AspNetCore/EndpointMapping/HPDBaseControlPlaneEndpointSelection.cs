namespace HPD.Base.AspNetCore;

/// <summary>
/// Selects BASE endpoint families for an external control-plane security integration.
/// </summary>
/// <remarks>
/// This is an advanced integration SPI. Applications should use an owning security
/// package such as HPD.Base.Auth instead of calling the core mapper directly.
/// </remarks>
internal sealed record HPDBaseControlPlaneEndpointSelection
{
    /// <summary>Gets whether record endpoints are selected.</summary>
    public bool MapRecords { get; init; } = true;
    /// <summary>Gets whether registered reads are selected.</summary>
    public bool MapRegisteredReads { get; init; } = true;
    /// <summary>Gets whether administrative inspection is selected.</summary>
    public bool MapAdministration { get; init; } = true;
    /// <summary>Gets whether policy explanation is selected.</summary>
    public bool MapPolicyExplain { get; init; } = true;
    /// <summary>Gets whether files are selected.</summary>
    public bool MapFiles { get; init; }
    /// <summary>Gets whether realtime is selected.</summary>
    public bool MapRealtime { get; init; }
}

namespace HPD.Base.AspNetCore;

/// <summary>Configures host-selected Public BASE endpoints.</summary>
public sealed class HPDBasePublicEndpointOptions
{
    /// <summary>Gets the common route prefix.</summary>
    public string RoutePrefix { get; set; } = "/base";
    /// <summary>Gets the public metadata exposure.</summary>
    public HPDBasePublicMetadataMode MetadataMode { get; set; } = HPDBasePublicMetadataMode.Minimal;
    /// <summary>Gets whether health is mapped.</summary>
    public bool MapHealth { get; set; } = true;
    /// <summary>Gets whether diagnostics are mapped.</summary>
    public bool MapDiagnostics { get; set; }
}

/// <summary>Configures host-authorized Application BASE endpoints.</summary>
public sealed class HPDBaseApplicationEndpointOptions
{
    /// <summary>Gets the common route prefix.</summary>
    public string RoutePrefix { get; init; } = "/base";
    /// <summary>Gets the required host ASP.NET authorization policy.</summary>
    public required string AuthorizationPolicy { get; init; }
    /// <summary>Gets whether record endpoints are mapped.</summary>
    public bool MapRecords { get; init; } = true;
    /// <summary>Gets whether Public-exposure registered reads are mapped.</summary>
    public bool MapRegisteredReads { get; init; } = true;
    /// <summary>Gets whether file endpoints are mapped.</summary>
    public bool MapFiles { get; init; }
    /// <summary>Gets whether realtime is mapped.</summary>
    public bool MapRealtime { get; init; }
}

/// <summary>Controls the public BASE metadata surface.</summary>
public enum HPDBasePublicMetadataMode
{
    /// <summary>Maps manifest, capabilities, schema, and collection metadata.</summary>
    Full,
    /// <summary>Maps compact manifest and capabilities metadata.</summary>
    Minimal,
    /// <summary>Maps no public metadata.</summary>
    Disabled
}

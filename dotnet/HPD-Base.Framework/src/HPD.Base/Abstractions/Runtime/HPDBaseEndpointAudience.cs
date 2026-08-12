namespace HPD.Base;

/// <summary>Identifies the audience of one BASE endpoint, session, or generated read.</summary>
public enum HPDBaseEndpointAudience
{
    /// <summary>Unauthenticated host-selected operational discovery.</summary>
    Public,
    /// <summary>Ordinary host-authorized application-data access.</summary>
    Application,
    /// <summary>Validated administrative control-plane access.</summary>
    ControlPlane,
}

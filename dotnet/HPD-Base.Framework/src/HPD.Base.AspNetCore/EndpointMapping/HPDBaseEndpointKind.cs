namespace HPD.Base.AspNetCore;

/// <summary>
/// Identifies the HTTP route family requesting a principal context.
/// </summary>
public enum HPDBaseEndpointKind
{
    /// <summary>Public metadata route.</summary>
    PublicMetadata,

    /// <summary>Admin metadata route.</summary>
    AdminMetadata,

    /// <summary>User-facing record route.</summary>
    Records
}

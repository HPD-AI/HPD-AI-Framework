namespace HPD.Base;

/// <summary>
/// Describes the intended audience for descriptor, schema, event, health, and diagnostic data.
/// </summary>
public enum VisibilityLevel
{
    /// <summary>Identifies public.</summary>
Public,
    /// <summary>Identifies authenticated.</summary>
Authenticated,
    /// <summary>Identifies admin.</summary>
Admin,
    /// <summary>Identifies internal.</summary>
Internal
}

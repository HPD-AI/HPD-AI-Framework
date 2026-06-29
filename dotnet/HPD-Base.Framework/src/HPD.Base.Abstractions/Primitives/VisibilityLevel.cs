namespace HPD.Base;

/// <summary>
/// Describes the intended audience for descriptor, schema, event, health, and diagnostic data.
/// </summary>
public enum VisibilityLevel
{
    Public,
    Authenticated,
    Admin,
    Internal
}

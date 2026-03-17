using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Requirements;

/// <summary>
/// Requirement: the authenticated user must have access to a specific HPD app.
/// The app ID may be supplied directly on the requirement or, if omitted, resolved
/// from the route value named <c>appId</c> at evaluation time.
/// </summary>
public class AppAccessRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// The app ID that the user must have access to.
    /// When <see langword="null"/> the handler falls back to the <c>appId</c> route value.
    /// </summary>
    public string? AppId { get; }

    /// <param name="appId">
    /// Fixed app ID, or <see langword="null"/> to read from the current route.
    /// </param>
    public AppAccessRequirement(string? appId = null) => AppId = appId;
}

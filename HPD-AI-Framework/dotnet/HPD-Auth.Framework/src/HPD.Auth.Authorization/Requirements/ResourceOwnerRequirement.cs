using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Requirements;

/// <summary>
/// Requirement: the authenticated user must own the resource, or be in the
/// <c>Admin</c> role.
/// </summary>
/// <remarks>
/// Use with <see cref="Microsoft.AspNetCore.Authorization.IAuthorizationService"/>
/// resource-based authorization by passing the resource object directly:
/// <code>
/// await _authService.AuthorizeAsync(User, document, new ResourceOwnerRequirement());
/// </code>
/// The resource must implement <see cref="IOwnable"/>.
/// </remarks>
public class ResourceOwnerRequirement : IAuthorizationRequirement { }

/// <summary>
/// Marks a domain object as having an owner that can be checked by
/// <see cref="ResourceOwnerRequirement"/>.
/// </summary>
public interface IOwnable
{
    /// <summary>The ID of the user who owns this resource.</summary>
    Guid OwnerId { get; }
}

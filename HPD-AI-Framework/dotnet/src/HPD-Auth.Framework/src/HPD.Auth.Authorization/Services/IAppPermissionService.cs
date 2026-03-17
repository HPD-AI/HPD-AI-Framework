namespace HPD.Auth.Authorization.Services;

/// <summary>
/// Determines whether a user has access to a specific HPD app.
/// </summary>
/// <remarks>
/// Consumers of HPD.Auth.Authorization must register an implementation of this
/// interface. It is called by <see cref="Handlers.AppAccessHandler"/> when the
/// <see cref="Requirements.AppAccessRequirement"/> is evaluated.
/// </remarks>
public interface IAppPermissionService
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="userId"/> has access to
    /// the app identified by <paramref name="appId"/>; otherwise <see langword="false"/>.
    /// </summary>
    /// <param name="userId">The ID of the user requesting access.</param>
    /// <param name="appId">The unique identifier of the HPD app.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<bool> UserHasAppAccessAsync(Guid userId, string appId, CancellationToken ct = default);
}

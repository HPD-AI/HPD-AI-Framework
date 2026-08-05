using Microsoft.AspNetCore.Http;

namespace HPD.Auth.ControlPlane;

public interface IAuthenticatedActorProjector
{
    ValueTask<AuthenticatedActorProjection> ProjectAsync(
        HttpContext context,
        string controlPlaneProfile,
        CancellationToken cancellationToken = default);
}

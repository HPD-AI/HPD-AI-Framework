using HPD.Auth.ControlPlane;
using HPD.Auth.Core.Audit;

namespace HPD.Gateway.ControlPlane.HPDAuth;

/// <summary>Translates bounded HPD.Auth attribution into Gateway-owned command attribution.</summary>
public static class GatewayManagementActorProjection
{
    public static GatewayManagementActor ToGatewayActor(
        this AuthenticatedActorProjection projection,
        string authorizationPolicy)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationPolicy);
        return new GatewayManagementActor(
            new string(projection.ActorId.AsSpan()),
            new string(projection.AuthenticationProfile.AsSpan()),
            new string(authorizationPolicy.AsSpan()));
    }

    public static string RequireGatewayCorrelation(this IAuthCorrelationContext correlation) =>
        correlation.CorrelationId is { } value
            ? new string(value.AsSpan())
            : throw new InvalidOperationException("A control-plane correlation identifier is required.");
}

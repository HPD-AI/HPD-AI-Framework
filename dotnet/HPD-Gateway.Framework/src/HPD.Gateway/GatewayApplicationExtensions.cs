using HPD.Gateway.Core;
using HPD.Gateway.Status;
using HPD.Gateway.Yarp;
using HPD.Gateway.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway;

public static class GatewayApplicationExtensions
{
    public static WebApplication MapHpdGateway(this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var marker = application.Services.GetRequiredService<HpdGatewayMappingMarker>();
        if (marker.IsMapped)
            throw new InvalidOperationException("MapHpdGateway may be called only once for a governed host.");
        foreach (var participant in application.Services
            .GetServices<IGatewayApplicationPipelineParticipant>()
            .OrderBy(static participant => participant.Order))
            participant.Configure(application);
        application.UseHpdGatewayListenerRoles();
        application.MapHpdGatewayHealth();
        application.MapHpdGatewayReverseProxy()
            .WithHpdGatewayEndpointRole(GatewayListenerRole.DataPlane, "gateway-data");
        return application;
    }
}

internal sealed class GatewayNativePolicyPipeline(GatewayCompositionState state) :
    IGatewayApplicationPipelineParticipant
{
    public int Order => 100;

    public void Configure(IApplicationBuilder application)
    {
        if (!state.CorsPolicies.IsEmpty) application.UseCors();
        if (!state.AuthorizationPolicies.IsEmpty) application.UseAuthorization();
        if (!state.TrafficAdmissionPolicies.IsEmpty) application.UseRateLimiter();
        if (!state.RequestTimeoutPolicies.IsEmpty) application.UseRequestTimeouts();
    }
}

internal sealed class HpdGatewayMappingMarker : IGatewayEndpointMappingParticipant
{
    public bool IsMapped { get; private set; }
    void IGatewayEndpointMappingParticipant.MarkMapped() => IsMapped = true;
}

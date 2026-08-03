using HPD.Gateway.Core;
using HPD.Gateway.Status;
using HPD.Gateway.Yarp;
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
            .GetServices<IGatewayApplicationPipelineParticipant>())
            participant.Configure(application);
        application.MapHpdGatewayHealth();
        application.MapHpdGatewayReverseProxy();
        return application;
    }
}

internal sealed class HpdGatewayMappingMarker : IGatewayEndpointMappingParticipant
{
    public bool IsMapped { get; private set; }
    void IGatewayEndpointMappingParticipant.MarkMapped() => IsMapped = true;
}

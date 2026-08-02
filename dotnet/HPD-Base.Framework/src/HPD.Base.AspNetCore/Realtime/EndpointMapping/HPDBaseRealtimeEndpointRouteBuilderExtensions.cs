using HPD.Base;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

/// <summary>Represents a hpdbase realtime endpoint route builder extensions.</summary>
public static class HPDBaseRealtimeEndpointRouteBuilderExtensions
{
    /// <summary>Executes the map hpdbase realtime operation.</summary>
    public static IEndpointRouteBuilder MapHPDBaseRealtime(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(BaseRealtimeRoutes.WebSocket, (RequestDelegate)HandleWebSocketAsync)
            .WithName(BaseRealtimeRouteIds.WebSocket)
            .WithDisplayName("HPD.BASE realtime WebSocket")
            .WithMetadata(new BaseRealtimeWebSocketOpenApiMetadata());

        return endpoints;
    }

    private static Task HandleWebSocketAsync(HttpContext context) =>
        context.RequestServices.GetRequiredService<BaseRealtimeWebSocketEndpoint>().HandleAsync(context);
}

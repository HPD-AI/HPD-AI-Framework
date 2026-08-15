using HPD.Base;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

/// <summary>Represents a hpdbase realtime endpoint route builder extensions.</summary>
internal static class HPDBaseRealtimeEndpointRouteBuilderExtensions
{
    internal static void MapCore(IEndpointRouteBuilder endpoints, HPDBaseEndpointAudience audience, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null) =>
        endpoints.MapGet("/realtime/v2/socket", (RequestDelegate)HandleWebSocketAsync)
            .WithHPDBaseEndpoint(BaseRealtimeRouteIds.WebSocketV2, audience, HPDBaseEndpointOperation.RealtimeSubscribe, HPDBaseCapabilities.RealtimeSubscribe, convention)
            .WithName(BaseRealtimeRouteIds.WebSocketV2)
            .WithDisplayName("HPD.BASE realtime WebSocket")
            .WithMetadata(new BaseRealtimeWebSocketOpenApiMetadata());

    private static Task HandleWebSocketAsync(HttpContext context) =>
        context.RequestServices.GetRequiredService<BaseRealtimeWebSocketEndpoint>().HandleAsync(context);
}

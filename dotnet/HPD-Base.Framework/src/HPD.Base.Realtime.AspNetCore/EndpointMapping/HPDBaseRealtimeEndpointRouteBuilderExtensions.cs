using System.Text.Json;
using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.AspNetCore.Http;
using HPD.Base.Realtime.Configuration;
using HPD.Base.Realtime.AspNetCore.Observability;
using HPD.Base.Realtime.AspNetCore.Descriptors;
using HPD.Base.Realtime.Feeds;
using HPD.Base.Realtime.Serialization;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.Realtime.AspNetCore.EndpointMapping;

public static class HPDBaseRealtimeEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapHPDBaseRealtime(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(BaseRealtimeRoutes.WebSocket, (RequestDelegate)HandleWebSocketAsync)
            .WithName(BaseRealtimeRouteIds.WebSocket)
            .WithDisplayName("HPD.BASE realtime WebSocket")
            .WithMetadata(new BaseRealtimeWebSocketOpenApiMetadata());

        return endpoints;
    }

    private static async Task HandleWebSocketAsync(
        HttpContext context)
    {
        using var acceptActivity = HPDBaseRealtimeAspNetCoreTelemetry.StartAccept();
        var services = context.RequestServices;
        var feeds = services.GetRequiredService<IBaseRealtimeFeedSource>();
        var principals = services.GetRequiredService<IBaseHttpPrincipalContextFactory>();
        var json = services.GetRequiredService<IBaseJsonOptionsProvider>();
        var options = services.GetRequiredService<IOptions<BaseRealtimeOptions>>();
        var stats = services.GetRequiredService<BaseRealtimeStats>();

        if (!options.Value.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await WriteErrorAsync(context, BaseRealtimeErrorCodes.Disabled, "HPD.BASE realtime is disabled.").ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(acceptActivity, "disabled");
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(context, BaseRealtimeErrorCodes.ProtocolInvalid, "This endpoint requires a WebSocket upgrade.").ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(acceptActivity, "rejected");
            return;
        }

        if (stats.ActiveConnections >= options.Value.Limits.MaxConnections)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await WriteErrorAsync(context, BaseRealtimeErrorCodes.TooManyConnections, "The realtime connection limit has been reached.").ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(acceptActivity, "rejected");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var principal = await principals.CreateAsync(context, HPDBaseEndpointKind.Records, context.RequestAborted).ConfigureAwait(false);
        var session = new BaseRealtimeWebSocketSession(socket, feeds, json.Options, options.Value, stats, principal);
        HPDBaseRealtimeAspNetCoreTelemetry.Finish(acceptActivity, "ok");
        await session.RunAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(HttpContext context, string code, string message)
    {
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            Error(code, message),
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeError,
            context.RequestAborted).ConfigureAwait(false);
    }

    private static BaseRealtimeError Error(string code, string message) => new()
    {
        Code = code,
        Message = message
    };
}

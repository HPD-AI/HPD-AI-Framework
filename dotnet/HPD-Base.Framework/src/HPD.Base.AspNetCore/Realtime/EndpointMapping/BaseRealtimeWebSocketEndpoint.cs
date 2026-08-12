using System.Text.Json;
using HPD.Base.AspNetCore;
using HPD.Base;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

internal sealed class BaseRealtimeWebSocketEndpoint(
    ILogger<BaseRealtimeWebSocketEndpoint> logger,
    ILogger<BaseRealtimeWebSocketSession> sessionLogger)
{
    /// <summary>Executes the handle async operation.</summary>
    public async Task HandleAsync(HttpContext context)
    {
        using var acceptActivity = HPDBaseRealtimeAspNetCoreTelemetry.StartAccept();
        var services = context.RequestServices;
        var feeds = services.GetRequiredService<IBaseRealtimeFeedSource>();
        var principals = services.GetRequiredService<IBaseHttpPrincipalContextFactory>();
        var json = services.GetRequiredService<IBaseJsonOptionsProvider>();
        var options = services.GetRequiredService<IOptions<BaseRealtimeOptions>>();
        var stats = services.GetRequiredService<BaseRealtimeStats>();
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var liveQueries = services.GetService<BaseRealtimeLiveQueryTransport>();

        if (!options.Value.Enabled)
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketConnectionRejected(logger, BaseRealtimeErrorCodes.Disabled);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await WriteErrorAsync(context, BaseRealtimeErrorCodes.Disabled, "HPD.BASE realtime is disabled.").ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(acceptActivity, "disabled");
            acceptActivity?.Stop();
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketConnectionRejected(logger, BaseRealtimeErrorCodes.ProtocolInvalid);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(context, BaseRealtimeErrorCodes.ProtocolInvalid, "This endpoint requires a WebSocket upgrade.").ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(acceptActivity, "rejected");
            acceptActivity?.Stop();
            return;
        }

        if (!stats.TryRecordConnectionOpened(options.Value.Limits.MaxConnections))
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketConnectionRejected(logger, BaseRealtimeErrorCodes.TooManyConnections);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await WriteErrorAsync(context, BaseRealtimeErrorCodes.TooManyConnections, "The realtime connection limit has been reached.").ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(acceptActivity, "rejected");
            acceptActivity?.Stop();
            return;
        }

        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            var principal = await principals.CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
            var session = new BaseRealtimeWebSocketSession(
                socket, feeds, json.Options, options.Value, stats, principal, sessionLogger, timeProvider, liveQueries);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(acceptActivity, "ok");
            acceptActivity?.Stop();
            await session.RunAsync(context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            stats.RecordConnectionClosed();
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, string code, string message)
    {
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new BaseRealtimeError { Code = code, Message = message },
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeError,
            context.RequestAborted).ConfigureAwait(false);
    }
}

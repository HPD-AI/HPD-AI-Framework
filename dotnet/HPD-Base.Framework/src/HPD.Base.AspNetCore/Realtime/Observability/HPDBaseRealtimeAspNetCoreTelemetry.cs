using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base;

namespace HPD.Base.AspNetCore;

internal static class HPDBaseRealtimeAspNetCoreTelemetry
{
    private static readonly Counter<long> MessagesReceived = HPDBaseRealtimeAspNetCoreObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeMessagesReceived,
        unit: "{message}",
        description: "Counts HPD.BASE realtime WebSocket messages received.");

    private static readonly Counter<long> MessagesSent = HPDBaseRealtimeAspNetCoreObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeMessagesSent,
        unit: "{message}",
        description: "Counts HPD.BASE realtime WebSocket messages sent.");

    private static readonly Histogram<long> MessageBytes = HPDBaseRealtimeAspNetCoreObservability.Meter.CreateHistogram<long>(
        HPDBaseTelemetryInstruments.RealtimeMessageBytes,
        unit: "By",
        description: "Records HPD.BASE realtime WebSocket message sizes.");

    public static Activity? StartAccept()
    {
        var activity = HPDBaseRealtimeAspNetCoreObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RealtimeWebSocketAccept, ActivityKind.Server);
        SetCommon(activity);
        activity?.SetTag(HPDBaseTelemetryTags.RealtimeTransport, HPDBaseTelemetryValues.TransportWebSocket);
        return activity;
    }

    public static Activity? StartConnection()
    {
        var activity = HPDBaseRealtimeAspNetCoreObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RealtimeConnection, ActivityKind.Internal);
        SetCommon(activity);
        activity?.SetTag(HPDBaseTelemetryTags.RealtimeTransport, HPDBaseTelemetryValues.TransportWebSocket);
        return activity;
    }

    public static Activity? StartJoin(string channelKind)
    {
        var activity = HPDBaseRealtimeAspNetCoreObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RealtimeChannelJoin, ActivityKind.Internal);
        SetCommon(activity);
        activity?.SetTag(HPDBaseTelemetryTags.RealtimeChannelKind, channelKind);
        return activity;
    }

    public static Activity? StartLeave()
    {
        var activity = HPDBaseRealtimeAspNetCoreObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RealtimeChannelLeave, ActivityKind.Internal);
        SetCommon(activity);
        return activity;
    }

    public static Activity? StartSend(string channelKind)
    {
        var activity = HPDBaseRealtimeAspNetCoreObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RealtimeEventSend, ActivityKind.Internal);
        SetCommon(activity);
        activity?.SetTag(HPDBaseTelemetryTags.RealtimeChannelKind, channelKind);
        return activity;
    }

    public static void Finish(Activity? activity, string status)
    {
        activity?.SetTag(HPDBaseTelemetryTags.ResultStatus, status);
        if (status == "error")
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
    }

    public static void RecordReceived(long bytes)
    {
        MessagesReceived.Add(1, MessageTags("received"));
        if (bytes >= 0)
        {
            MessageBytes.Record(bytes, MessageTags("received"));
        }
    }

    public static void RecordSent(long bytes)
    {
        MessagesSent.Add(1, MessageTags("sent"));
        if (bytes >= 0)
        {
            MessageBytes.Record(bytes, MessageTags("sent"));
        }
    }

    private static void SetCommon(Activity? activity)
    {
        activity?.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleRealtime);
    }

    private static TagList MessageTags(string kind) => new()
    {
        { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleRealtime },
        { HPDBaseTelemetryTags.RealtimeTransport, HPDBaseTelemetryValues.TransportWebSocket },
        { HPDBaseTelemetryTags.CountBucket, kind }
    };
}

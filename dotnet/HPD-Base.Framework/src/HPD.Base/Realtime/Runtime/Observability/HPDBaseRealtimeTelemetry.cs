using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Events;

namespace HPD.Base;

internal static class HPDBaseRealtimeTelemetry
{
    private static readonly object Gate = new();
    private static BaseRealtimeStats? s_stats;

    private static readonly Counter<long> ConnectionsOpened = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeConnectionsOpened,
        unit: "{connection}",
        description: "Counts HPD.BASE realtime opened connections.");

    private static readonly Counter<long> ConnectionsClosed = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeConnectionsClosed,
        unit: "{connection}",
        description: "Counts HPD.BASE realtime closed connections.");

    private static readonly Counter<long> ChannelsOpened = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeChannelsOpened,
        unit: "{channel}",
        description: "Counts HPD.BASE realtime opened channels.");

    private static readonly Counter<long> ChannelsClosed = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeChannelsClosed,
        unit: "{channel}",
        description: "Counts HPD.BASE realtime closed channels.");

    private static readonly Counter<long> EventsProjected = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeEventsProjected,
        unit: "{event}",
        description: "Counts HPD.BASE realtime projected events.");

    private static readonly Counter<long> PolicySkips = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimePolicySkips,
        unit: "{event}",
        description: "Counts HPD.BASE realtime events skipped by policy/projection.");

    private static readonly Counter<long> StreamOpenFailures = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeStreamOpenFailures,
        unit: "{error}",
        description: "Counts HPD.BASE realtime underlying stream open failures.");

    private static readonly Counter<long> SendFailures = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeSendFailures,
        unit: "{error}",
        description: "Counts HPD.BASE realtime send failures.");

    private static readonly Counter<long> ReceiveIdleTimeouts = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeReceiveIdleTimeouts,
        unit: "{error}",
        description: "Counts HPD.BASE realtime receive-idle timeouts.");

    private static readonly Counter<long> JoinRateRejections = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeJoinRateRejections,
        unit: "{join}",
        description: "Counts HPD.BASE realtime channel joins rejected by the per-connection rate limit.");

    private static readonly Counter<long> SlowConsumerTerminations = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeSlowConsumerTerminations,
        unit: "{channel}",
        description: "Counts HPD.BASE realtime channels terminated because their consumers were too slow.");

    private static readonly Counter<long> PayloadDrops = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimePayloadDrops,
        unit: "{message}",
        description: "Counts HPD.BASE realtime payload drops.");

    private static readonly Counter<long> DurableJournalReads = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeDurableJournalReads,
        unit: "{read}",
        description: "Counts bounded HPD.BASE durable journal reads.");

    private static readonly Counter<long> DurableEventsProjected = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeDurableEventsProjected,
        unit: "{event}",
        description: "Counts policy-safe HPD.BASE durable event projections.");

    private static readonly Counter<long> DurableCursorRejections = HPDBaseRealtimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RealtimeDurableCursorRejections,
        unit: "{cursor}",
        description: "Counts rejected HPD.BASE durable cursors.");

    private static readonly Histogram<double> JoinDuration = HPDBaseRealtimeObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.RealtimeJoinDuration,
        unit: "s",
        description: "Records HPD.BASE realtime channel join/open duration.");

    static HPDBaseRealtimeTelemetry()
    {
        HPDBaseRealtimeObservability.Meter.CreateObservableGauge(
            HPDBaseTelemetryInstruments.RealtimeConnectionsActive,
            () => Measurement(s_stats?.ActiveConnections ?? 0, "connection"),
            unit: "{connection}",
            description: "Reports active HPD.BASE realtime connections.");
        HPDBaseRealtimeObservability.Meter.CreateObservableGauge(
            HPDBaseTelemetryInstruments.RealtimeChannelsActive,
            () => Measurement(s_stats?.ActiveChannels ?? 0, "channel"),
            unit: "{channel}",
            description: "Reports active HPD.BASE realtime channels.");
    }

    /// <summary>Executes the register stats operation.</summary>
    public static void RegisterStats(BaseRealtimeStats stats)
    {
        lock (Gate)
        {
            s_stats = stats;
        }
    }

    /// <summary>Executes the trace join async operation.</summary>
    public static async ValueTask<T> TraceJoinAsync<T>(string channelKind, Func<ValueTask<T>> invoke)
    {
        using var activity = Start(HPDBaseTelemetrySpans.RealtimeChannelJoin, channelKind);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        Finish(activity, channelKind, StatusFor(result), startedAt);
        return result;
    }

    /// <summary>Executes the record event projected operation.</summary>
    public static void RecordEventProjected() => EventsProjected.Add(1, ChannelTags("recordChanges"));

    /// <summary>Executes the record connection opened operation.</summary>
    public static void RecordConnectionOpened() => ConnectionsOpened.Add(1, ConnectionTags());
    /// <summary>Executes the record connection closed operation.</summary>
    public static void RecordConnectionClosed() => ConnectionsClosed.Add(1, ConnectionTags());
    /// <summary>Executes the record channel opened operation.</summary>
    public static void RecordChannelOpened() => ChannelsOpened.Add(1, ChannelTags("recordChanges"));
    /// <summary>Executes the record channel closed operation.</summary>
    public static void RecordChannelClosed() => ChannelsClosed.Add(1, ChannelTags("recordChanges"));
    /// <summary>Executes the record policy skip operation.</summary>
    public static void RecordPolicySkip() => PolicySkips.Add(1, ChannelTags("recordChanges"));
    /// <summary>Executes the record stream open failure operation.</summary>
    public static void RecordStreamOpenFailure() => StreamOpenFailures.Add(1, ErrorTags("streamOpenFailure"));
    /// <summary>Executes the record send failure operation.</summary>
    public static void RecordSendFailure() => SendFailures.Add(1, ErrorTags("sendFailure"));
    /// <summary>Executes the record receive idle timeout operation.</summary>
    public static void RecordReceiveIdleTimeout() => ReceiveIdleTimeouts.Add(1, ErrorTags("receiveIdleTimeout"));
    /// <summary>Executes the record join rate rejection operation.</summary>
    public static void RecordJoinRateRejection() => JoinRateRejections.Add(1, ErrorTags("joinRateLimited"));
    /// <summary>Executes the record slow consumer termination operation.</summary>
    public static void RecordSlowConsumerTermination() => SlowConsumerTerminations.Add(1, ErrorTags("slowConsumer"));
    /// <summary>Executes the record payload drop operation.</summary>
    public static void RecordPayloadDrop() => PayloadDrops.Add(1, MessageTags("dropped"));
    /// <summary>Executes the record durable journal read operation.</summary>
    public static void RecordDurableJournalRead() => DurableJournalReads.Add(1, ChannelTags("recordChanges"));
    /// <summary>Executes the record durable event projected operation.</summary>
    public static void RecordDurableEventProjected() => DurableEventsProjected.Add(1, ChannelTags("recordChanges"));
    /// <summary>Executes the record durable cursor rejection operation.</summary>
    public static void RecordDurableCursorRejection() => DurableCursorRejections.Add(1, ErrorTags("cursorRejected"));

    private static Activity? Start(string spanName, string channelKind)
    {
        var activity = HPDBaseRealtimeObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleRealtime);
        activity.SetTag(HPDBaseTelemetryTags.RealtimeChannelKind, channelKind);
        return activity;
    }

    private static void Finish(Activity? activity, string channelKind, string status, long startedAt)
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

        JoinDuration.Record((double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency, ChannelTags(channelKind));
    }

    private static string StatusFor<T>(T result)
    {
        if (result is AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>> opened)
        {
            return opened.Succeeded ? "ok" : "error";
        }

        return "ok";
    }

    private static Measurement<long> Measurement(long value, string kind) => new(value, CommonTags(kind));

    private static TagList ConnectionTags() => CommonTags("connection");

    private static TagList ChannelTags(string channelKind)
    {
        var tags = CommonTags("channel");
        tags.Add(HPDBaseTelemetryTags.RealtimeChannelKind, channelKind);
        return tags;
    }

    private static TagList MessageTags(string kind)
    {
        var tags = CommonTags("message");
        tags.Add(HPDBaseTelemetryTags.CountBucket, kind);
        return tags;
    }

    private static TagList ErrorTags(string code)
    {
        var tags = CommonTags("error");
        tags.Add(HPDBaseTelemetryTags.ErrorCode, code);
        return tags;
    }

    private static TagList CommonTags(string kind) => new()
    {
        { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleRealtime },
        { HPDBaseTelemetryTags.CountBucket, kind }
    };
}

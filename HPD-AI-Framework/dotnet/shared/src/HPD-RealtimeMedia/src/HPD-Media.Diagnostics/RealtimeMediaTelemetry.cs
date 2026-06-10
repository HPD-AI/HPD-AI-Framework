#nullable enable

using System.Diagnostics;
using HPD.Events;
using HPD.Events.Struct;

namespace HPD.Media.Diagnostics;

/// <summary>
/// Identifies a realtime media telemetry sample as diagnostic struct-event data.
/// </summary>
public interface IRealtimeMediaSample : IStructEvent
{
}

/// <summary>
/// RTP packet-loss telemetry sample.
/// </summary>
public readonly struct RtpLossSample : IRealtimeMediaSample
{
    public EventKind Kind => EventKind.Diagnostic;
    public long SequenceNumber { get; init; }
    public long TimestampNs { get; init; }
    public required uint Ssrc { get; init; }
    public required ushort SequenceStart { get; init; }
    public required int LostPacketCount { get; init; }
    public uint? ExpectedTimestamp { get; init; }
}

/// <summary>
/// RTP reorder-buffer depth telemetry sample.
/// </summary>
public readonly struct RtpReorderDepthSample : IRealtimeMediaSample
{
    public EventKind Kind => EventKind.Diagnostic;
    public long SequenceNumber { get; init; }
    public long TimestampNs { get; init; }
    public required uint Ssrc { get; init; }
    public required int Depth { get; init; }
    public required int Capacity { get; init; }
}

/// <summary>
/// RTCP interarrival jitter telemetry sample.
/// </summary>
public readonly struct RtcpJitterSample : IRealtimeMediaSample
{
    public EventKind Kind => EventKind.Diagnostic;
    public long SequenceNumber { get; init; }
    public long TimestampNs { get; init; }
    public required uint ReporterSsrc { get; init; }
    public required uint RemoteSsrc { get; init; }
    public required uint InterarrivalJitter { get; init; }
}

/// <summary>
/// SRTP or SRTCP packet reject telemetry sample.
/// </summary>
public readonly struct SrtpRejectSample : IRealtimeMediaSample
{
    public EventKind Kind => EventKind.Diagnostic;
    public long SequenceNumber { get; init; }
    public long TimestampNs { get; init; }
    public required uint Ssrc { get; init; }
    public required SrtpRejectKind RejectKind { get; init; }
    public required bool IsRtcp { get; init; }
}

/// <summary>
/// Classifies SRTP/SRTCP packet rejection samples.
/// </summary>
public enum SrtpRejectKind
{
    AuthenticationFailed = 0,
    ReplayRejected = 1,
    Duplicate = 2,
    InvalidPacket = 3,
    WrongSsrc = 4,
    UnsupportedProfile = 5,
    MkiRejected = 6
}

/// <summary>
/// Codec encode/decode timing telemetry sample.
/// </summary>
public readonly struct CodecTimingSample : IRealtimeMediaSample
{
    public EventKind Kind => EventKind.Diagnostic;
    public long SequenceNumber { get; init; }
    public long TimestampNs { get; init; }
    public required CodecOperation Operation { get; init; }
    public required int Encoding { get; init; }
    public required long ElapsedNanoseconds { get; init; }
    public required int InputBytes { get; init; }
    public required int OutputBytes { get; init; }
}

/// <summary>
/// Identifies a codec timing operation.
/// </summary>
public enum CodecOperation
{
    Encode = 0,
    Decode = 1
}

/// <summary>
/// Datagram queue-depth telemetry sample.
/// </summary>
public readonly struct DatagramQueueDepthSample : IRealtimeMediaSample
{
    public EventKind Kind => EventKind.Diagnostic;
    public long SequenceNumber { get; init; }
    public long TimestampNs { get; init; }
    public required int Depth { get; init; }
    public required int Capacity { get; init; }
    public required int DroppedCount { get; init; }
}

/// <summary>
/// Audio pump cycle telemetry sample.
/// </summary>
public readonly struct AudioPumpCycleSample : IRealtimeMediaSample
{
    public EventKind Kind => EventKind.Diagnostic;
    public long SequenceNumber { get; init; }
    public long TimestampNs { get; init; }
    public required int Operations { get; init; }
    public required long ElapsedNanoseconds { get; init; }
    public required int ScratchBytes { get; init; }
}

/// <summary>
/// Runs one bounded realtime media pump cycle.
/// </summary>
public delegate int RealtimeMediaPumpOperation(Span<byte> scratch, int maxOperations);

/// <summary>
/// Emits realtime telemetry for bounded media pump cycles.
/// </summary>
public static class RealtimeMediaPumpTelemetry
{
    /// <summary>
    /// Runs one pump cycle through a cached operation and emits an audio-pump cycle sample.
    /// </summary>
    public static int Pump(
        RealtimeMediaPumpOperation operation,
        Span<byte> scratch,
        int maxOperations,
        in RealtimeMediaTelemetryEmitters telemetry)
    {
        ArgumentNullException.ThrowIfNull(operation);

        long started = Stopwatch.GetTimestamp();
        int operations = operation(scratch, maxOperations);
        long elapsedTicks = Stopwatch.GetTimestamp() - started;

        _ = telemetry.AudioPumpCycle.Emit(new AudioPumpCycleSample
        {
            Operations = operations,
            ElapsedNanoseconds = elapsedTicks * 1_000_000_000L / Stopwatch.Frequency,
            ScratchBytes = scratch.Length
        });

        return operations;
    }
}

/// <summary>
/// Cached realtime media telemetry emitters.
/// </summary>
public readonly struct RealtimeMediaTelemetryEmitters
{
    public required StructEventEmitter<RtpLossSample> RtpLoss { get; init; }
    public required StructEventEmitter<RtpReorderDepthSample> RtpReorderDepth { get; init; }
    public required StructEventEmitter<RtcpJitterSample> RtcpJitter { get; init; }
    public required StructEventEmitter<SrtpRejectSample> SrtpReject { get; init; }
    public required StructEventEmitter<CodecTimingSample> CodecTiming { get; init; }
    public required StructEventEmitter<DatagramQueueDepthSample> DatagramQueueDepth { get; init; }
    public required StructEventEmitter<AudioPumpCycleSample> AudioPumpCycle { get; init; }
}

/// <summary>
/// Creates cached struct-event routes and emitters for realtime media telemetry.
/// </summary>
public static class RealtimeMediaTelemetry
{
    /// <summary>
    /// Gets the route options used by hot-path media telemetry.
    /// </summary>
    public static StructEventRouteOptions RouteOptions { get; } = new()
    {
        ConcurrencyMode = StructEventConcurrencyMode.MultiProducerMultiConsumer,
        StatsMode = StructEventStatsMode.None
    };

    /// <summary>
    /// Creates cached emitters for the mandatory first realtime media telemetry routes.
    /// </summary>
    public static RealtimeMediaTelemetryEmitters CreateEmitters(IStructEventHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        return new RealtimeMediaTelemetryEmitters
        {
            RtpLoss = hub.Route<RtpLossSample>(RouteOptions).CreateEmitter(),
            RtpReorderDepth = hub.Route<RtpReorderDepthSample>(RouteOptions).CreateEmitter(),
            RtcpJitter = hub.Route<RtcpJitterSample>(RouteOptions).CreateEmitter(),
            SrtpReject = hub.Route<SrtpRejectSample>(RouteOptions).CreateEmitter(),
            CodecTiming = hub.Route<CodecTimingSample>(RouteOptions).CreateEmitter(),
            DatagramQueueDepth = hub.Route<DatagramQueueDepthSample>(RouteOptions).CreateEmitter(),
            AudioPumpCycle = hub.Route<AudioPumpCycleSample>(RouteOptions).CreateEmitter()
        };
    }
}

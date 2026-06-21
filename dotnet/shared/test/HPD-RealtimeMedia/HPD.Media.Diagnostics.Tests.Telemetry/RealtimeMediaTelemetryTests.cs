#nullable enable

using HPD.Events;
using HPD.Events.Struct;
using HPD.Media.Diagnostics;

namespace HPD.Media.Diagnostics.Tests.Telemetry;

public sealed class RealtimeMediaTelemetryTests
{
    [Fact]
    public void CreateEmitters_UsesNoStatsStructRoutes()
    {
        using var hub = new StructEventHub();

        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        _ = emitters.RtpLoss.Emit(new RtpLossSample
        {
            TimestampNs = 1,
            Ssrc = 1,
            SequenceStart = 2,
            LostPacketCount = 1
        });
        IReadOnlyList<StructEventRouteStats> routeStats = hub.GetRouteStats();

        Assert.Equal(7, routeStats.Count);
        Assert.Contains(routeStats, static stats => stats.EventType == typeof(RtpLossSample) && stats.Emitted == 0);
    }

    [Fact]
    public void RtpLossSample_EmitsThroughCachedStructRoute()
    {
        using var hub = new StructEventHub();
        StructEventRoute<RtpLossSample> route = hub.Route<RtpLossSample>(RealtimeMediaTelemetry.RouteOptions);
        using StructEventInbox<RtpLossSample> inbox = route.CreateInbox(new StructEventInboxOptions
        {
            Capacity = 4,
            OverflowMode = StructEventOverflowMode.DropOldest
        });
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        var sample = new RtpLossSample
        {
            TimestampNs = 123,
            Ssrc = 42,
            SequenceStart = 10,
            LostPacketCount = 3,
            ExpectedTimestamp = 960
        };

        StructEventEmitResult result = emitters.RtpLoss.Emit(sample);
        bool read = inbox.TryRead(out RtpLossSample observed);

        Assert.Equal(StructEventEmitStatus.Accepted, result.Status);
        Assert.True(read);
        Assert.Equal(EventKind.Diagnostic, observed.Kind);
        Assert.Equal(sample.Ssrc, observed.Ssrc);
        Assert.Equal(sample.SequenceStart, observed.SequenceStart);
        Assert.Equal(sample.LostPacketCount, observed.LostPacketCount);
    }

    [Fact]
    public void CachedEmitters_NoSubscribers_AllocateZeroAfterWarmup()
    {
        using var hub = new StructEventHub();
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        var sample = new CodecTimingSample
        {
            TimestampNs = 456,
            Operation = CodecOperation.Decode,
            Encoding = 2,
            ElapsedNanoseconds = 1000,
            InputBytes = 24,
            OutputBytes = 960
        };

        for (int i = 0; i < 1_000; i++)
        {
            _ = emitters.CodecTiming.Emit(sample);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (emitters.CodecTiming.Emit(sample).Status != StructEventEmitStatus.NoSubscribers)
            {
                throw new InvalidOperationException("Unexpected struct event emit result.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void PumpTelemetry_EmitsAudioPumpCycleSample()
    {
        using var hub = new StructEventHub();
        StructEventRoute<AudioPumpCycleSample> route = hub.Route<AudioPumpCycleSample>(RealtimeMediaTelemetry.RouteOptions);
        using StructEventInbox<AudioPumpCycleSample> inbox = route.CreateInbox(new StructEventInboxOptions
        {
            Capacity = 4,
            OverflowMode = StructEventOverflowMode.DropOldest
        });
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        byte[] scratch = new byte[128];
        RealtimeMediaPumpOperation operation = PumpOnce;

        int operations = RealtimeMediaPumpTelemetry.Pump(operation, scratch, 7, emitters);
        bool read = inbox.TryRead(out AudioPumpCycleSample observed);

        Assert.Equal(7, operations);
        Assert.True(read);
        Assert.Equal(EventKind.Diagnostic, observed.Kind);
        Assert.Equal(7, observed.Operations);
        Assert.Equal(scratch.Length, observed.ScratchBytes);
        Assert.True(observed.ElapsedNanoseconds >= 0);
    }

    [Fact]
    public void PumpTelemetry_NoSubscribers_AllocatesZeroAfterWarmup()
    {
        using var hub = new StructEventHub();
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        byte[] scratch = new byte[64];
        RealtimeMediaPumpOperation operation = PumpOnce;

        for (int i = 0; i < 1_000; i++)
        {
            _ = RealtimeMediaPumpTelemetry.Pump(operation, scratch, 3, emitters);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            int operations = RealtimeMediaPumpTelemetry.Pump(operation, scratch, 3, emitters);
            if (operations != 3)
            {
                throw new InvalidOperationException("Unexpected pump result.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    private static int PumpOnce(Span<byte> scratch, int maxOperations)
    {
        scratch[0] = (byte)maxOperations;
        return maxOperations;
    }
}

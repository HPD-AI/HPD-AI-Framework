using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class ManualClockAudioOutputSinkTests
{
    [Fact]
    public async Task StartAsync_EmitsQueuedWithoutClaimingStarted()
    {
        var sink = new ManualClockAudioOutputSink();
        var outputFlowId = new OutputFlowId("output-manual-queued");
        var responseId = new ResponseId("response-manual-queued");
        var segmentId = new OutputSegmentId("segment-queued");

        var result = await StartAsync(sink, outputFlowId, responseId, segmentId, textLength: 12);
        var events = await ReadEventsAsync(sink, outputFlowId);

        Assert.Equal(OutputSinkStartDisposition.Accepted, result.Disposition);
        var queued = Assert.Single(events);
        Assert.IsType<OutputPlaybackQueuedEvent>(queued);
        Assert.Equal(segmentId, queued.SegmentId);
    }

    [Fact]
    public async Task AdvanceAsync_EmitsStartedThenProgressThenCompletedByManualTime()
    {
        var sink = new ManualClockAudioOutputSink();
        var outputFlowId = new OutputFlowId("output-manual-progress");
        var responseId = new ResponseId("response-manual-progress");
        var segmentId = new OutputSegmentId("segment-progress");

        await StartAsync(sink, outputFlowId, responseId, segmentId, textLength: 10, duration: TimeSpan.FromSeconds(2));
        _ = await ReadEventsAsync(sink, outputFlowId);

        await sink.AdvanceAsync(outputFlowId, TimeSpan.FromSeconds(1));
        var progressEvents = await ReadEventsAsync(sink, outputFlowId);

        Assert.Collection(progressEvents,
            started => Assert.IsType<OutputPlaybackStartedEvent>(started),
            progress =>
            {
                var progressEvent = Assert.IsType<OutputPlaybackProgressEvent>(progress);
                Assert.Equal(5, progressEvent.Cursor.PlayedTextLength);
                Assert.Equal(TimeSpan.FromSeconds(1), progressEvent.Cursor.PlayedDuration);
            });

        await sink.AdvanceAsync(outputFlowId, TimeSpan.FromSeconds(1));
        var completedEvents = await ReadEventsAsync(sink, outputFlowId);

        var completed = Assert.Single(completedEvents);
        var completedEvent = Assert.IsType<OutputPlaybackCompletedEvent>(completed);
        Assert.Equal(10, completedEvent.Cursor.PlayedTextLength);
        Assert.Equal(TimeSpan.FromSeconds(2), completedEvent.Cursor.PlayedDuration);
    }

    [Fact]
    public async Task InterruptAsync_ReturnsCurrentBoundaryAndClearsInterruptibleQueuedOutput()
    {
        var sink = new ManualClockAudioOutputSink();
        var outputFlowId = new OutputFlowId("output-manual-interrupt");
        var responseId = new ResponseId("response-manual-interrupt");
        var activeSegmentId = new OutputSegmentId("segment-active");
        var queuedSegmentId = new OutputSegmentId("segment-queued");

        await StartAsync(sink, outputFlowId, responseId, activeSegmentId, textLength: 20, duration: TimeSpan.FromSeconds(4));
        await StartAsync(sink, outputFlowId, responseId, queuedSegmentId, textLength: 10, duration: TimeSpan.FromSeconds(2));
        _ = await ReadEventsAsync(sink, outputFlowId);
        await sink.AdvanceAsync(outputFlowId, TimeSpan.FromSeconds(1));
        _ = await ReadEventsAsync(sink, outputFlowId);

        var boundary = await sink.InterruptAsync(outputFlowId);
        var interruptedEvents = await ReadEventsAsync(sink, outputFlowId);
        await sink.AdvanceAsync(outputFlowId, TimeSpan.FromSeconds(10));
        var postInterruptEvents = await ReadEventsAsync(sink, outputFlowId);

        Assert.Equal(5, boundary.PlayedTextLength);
        Assert.Equal(TimeSpan.FromSeconds(1), boundary.PlayedDuration);
        var interrupted = Assert.Single(interruptedEvents);
        Assert.IsType<OutputPlaybackInterruptedEvent>(interrupted);
        Assert.Empty(postInterruptEvents);
    }

    [Fact]
    public async Task InterruptAsync_PreservesQueuedUninterruptibleOutput()
    {
        var sink = new ManualClockAudioOutputSink();
        var outputFlowId = new OutputFlowId("output-manual-control");
        var responseId = new ResponseId("response-manual-control");
        var activeSegmentId = new OutputSegmentId("segment-active");
        var controlSegmentId = new OutputSegmentId("segment-control");

        await StartAsync(sink, outputFlowId, responseId, activeSegmentId, textLength: 20, duration: TimeSpan.FromSeconds(4));
        await StartAsync(
            sink,
            outputFlowId,
            responseId,
            controlSegmentId,
            textLength: 6,
            duration: TimeSpan.FromSeconds(1),
            interruptibility: OutputInterruptibility.Uninterruptible);
        _ = await ReadEventsAsync(sink, outputFlowId);
        await sink.AdvanceAsync(outputFlowId, TimeSpan.FromSeconds(1));
        _ = await ReadEventsAsync(sink, outputFlowId);

        _ = await sink.InterruptAsync(outputFlowId);
        _ = await ReadEventsAsync(sink, outputFlowId);
        await sink.AdvanceAsync(outputFlowId, TimeSpan.FromSeconds(1));
        var events = await ReadEventsAsync(sink, outputFlowId);

        Assert.Collection(events,
            started =>
            {
                Assert.IsType<OutputPlaybackStartedEvent>(started);
                Assert.Equal(controlSegmentId, started.SegmentId);
            },
            completed =>
            {
                var completedEvent = Assert.IsType<OutputPlaybackCompletedEvent>(completed);
                Assert.Equal(controlSegmentId, completedEvent.SegmentId);
                Assert.Equal(6, completedEvent.Cursor.PlayedTextLength);
            });
    }

    [Fact]
    public async Task FlushAsync_ReportsQueuedUnplayedSegmentsBeforeClearing()
    {
        var sink = new ManualClockAudioOutputSink();
        var outputFlowId = new OutputFlowId("output-manual-flush");
        var responseId = new ResponseId("response-manual-flush");
        var segmentId = new OutputSegmentId("segment-flush");

        await StartAsync(sink, outputFlowId, responseId, segmentId, textLength: 8);
        _ = await ReadEventsAsync(sink, outputFlowId);

        await sink.FlushAsync(outputFlowId);
        var events = await ReadEventsAsync(sink, outputFlowId);

        var cleared = Assert.Single(events);
        var clearedEvent = Assert.IsType<OutputPlaybackClearedEvent>(cleared);
        Assert.Equal(segmentId, clearedEvent.SegmentId);
        Assert.Equal(0, clearedEvent.Boundary.PlayedTextLength);
        Assert.Equal(TimeSpan.Zero, clearedEvent.Boundary.PlayedDuration);
    }

    [Fact]
    public async Task FailAsync_ReportsFailureWithoutClaimingPlayedAudio()
    {
        var sink = new ManualClockAudioOutputSink();
        var outputFlowId = new OutputFlowId("output-manual-fail");
        var responseId = new ResponseId("response-manual-fail");
        var segmentId = new OutputSegmentId("segment-fail");
        var error = new AudioErrorInfo
        {
            Code = "manual_sink_failed",
            Message = "Manual sink failure.",
            Category = "Playback"
        };

        await StartAsync(sink, outputFlowId, responseId, segmentId, textLength: 8);
        _ = await ReadEventsAsync(sink, outputFlowId);

        await sink.FailAsync(outputFlowId, segmentId, error);
        var events = await ReadEventsAsync(sink, outputFlowId);

        var failed = Assert.Single(events);
        var failedEvent = Assert.IsType<OutputPlaybackFailedEvent>(failed);
        Assert.Equal(segmentId, failedEvent.SegmentId);
        Assert.Equal(error, failedEvent.Error);
    }

    [Fact]
    public async Task StartAsync_EncodedStreamIsRejectedWithoutClaimingPlayback()
    {
        var sink = new ManualClockAudioOutputSink();
        var outputFlowId = new OutputFlowId("output-manual-encoded-rejected");
        var responseId = new ResponseId("response-manual-encoded-rejected");
        var segmentId = new OutputSegmentId("segment-encoded-rejected");

        var result = await sink.StartAsync(CreateStream(
            outputFlowId,
            responseId,
            segmentId,
            textLength: 8,
            OutputInterruptibility.Interruptible,
            OutputAudioPayloadKind.EncodedBytes,
            "audio/mpeg"));
        var events = await ReadEventsAsync(sink, outputFlowId);

        Assert.Equal(OutputSinkStartDisposition.Rejected, result.Disposition);
        Assert.Equal("ManualSinkUnsupportedOutputAudioPayload", result.Error?.Code);
        var failed = Assert.Single(events);
        var failedEvent = Assert.IsType<OutputPlaybackFailedEvent>(failed);
        Assert.Equal(segmentId, failedEvent.SegmentId);
        Assert.Equal("ManualSinkUnsupportedOutputAudioPayload", failedEvent.Error.Code);
    }

    [Fact]
    public async Task WriteAsync_DecodedFramesFeedDurationWhenCompletingStream()
    {
        var sink = new ManualClockAudioOutputSink();
        var outputFlowId = new OutputFlowId("output-manual-pcm-duration");
        var responseId = new ResponseId("response-manual-pcm-duration");
        var segmentId = new OutputSegmentId("segment-pcm-duration");

        await StartAsync(sink, outputFlowId, responseId, segmentId, textLength: 10);
        _ = await ReadEventsAsync(sink, outputFlowId);
        await sink.WriteAsync(CreateDecodedChunk(outputFlowId, responseId, segmentId, sequence: 0));
        await sink.WriteAsync(CreateDecodedChunk(outputFlowId, responseId, segmentId, sequence: 1, isFinal: true));
        await sink.CompleteAsync(new OutputAudioStreamCompletion
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Disposition = OutputAudioStreamDisposition.Completed,
            ChunkCount = 2,
            SizeBytes = 640,
            CompletedAt = DateTimeOffset.UnixEpoch
        });

        await sink.AdvanceAsync(outputFlowId, TimeSpan.FromMilliseconds(20));
        var events = await ReadEventsAsync(sink, outputFlowId);

        Assert.Collection(events,
            started => Assert.IsType<OutputPlaybackStartedEvent>(started),
            completed =>
            {
                var completedEvent = Assert.IsType<OutputPlaybackCompletedEvent>(completed);
                Assert.Equal(TimeSpan.FromMilliseconds(20), completedEvent.Cursor.PlayedDuration);
                Assert.Equal(10, completedEvent.Cursor.PlayedTextLength);
            });
    }

    private static async Task<OutputSinkStartResult> StartAsync(
        ManualClockAudioOutputSink sink,
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId,
        int textLength,
        TimeSpan? duration = null,
        OutputInterruptibility interruptibility = OutputInterruptibility.Interruptible)
    {
        var result = await sink.StartAsync(CreateStream(
            outputFlowId,
            responseId,
            segmentId,
            textLength,
            interruptibility));
        if (duration is not null)
        {
            await sink.CompleteAsync(new OutputAudioStreamCompletion
            {
                OutputFlowId = outputFlowId,
                ResponseId = responseId,
                SegmentId = segmentId,
                SegmentIndex = 0,
                Disposition = OutputAudioStreamDisposition.Completed,
                Duration = duration,
                CompletedAt = DateTimeOffset.UnixEpoch
            });
        }

        return result;
    }

    private static OutputAudioStream CreateStream(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId,
        int textLength,
        OutputInterruptibility interruptibility)
        => CreateStream(
            outputFlowId,
            responseId,
            segmentId,
            textLength,
            interruptibility,
            OutputAudioPayloadKind.DecodedPcmFrame,
            "audio/pcm");

    private static OutputAudioStream CreateStream(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId,
        int textLength,
        OutputInterruptibility interruptibility,
        OutputAudioPayloadKind payloadKind,
        string mediaType)
    {
        return new OutputAudioStream
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            SourceTextStart = 0,
            SourceTextLength = textLength,
            MediaType = mediaType,
            PayloadKind = payloadKind,
            StartedAt = DateTimeOffset.UnixEpoch,
            Interruptibility = interruptibility
        };
    }

    private static OutputAudioChunk CreateDecodedChunk(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId,
        int sequence,
        bool isFinal = false)
    {
        var frame = new HPD.Audio.Primitives.AudioFrame
        {
            Data = new byte[320],
            Format = new AudioFormat
            {
                SampleRate = 16_000,
                ChannelCount = 1,
                SampleFormat = AudioSampleFormat.Pcm16
            },
            SamplesPerChannel = 160,
            SequenceNumber = sequence
        };
        return new OutputAudioChunk
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Sequence = sequence,
            Payload = new DecodedOutputAudioFrame { Frame = frame },
            ObservedAt = DateTimeOffset.UnixEpoch,
            IsFinalChunk = isFinal
        };
    }

    private static async Task<IReadOnlyList<OutputPlaybackEvent>> ReadEventsAsync(
        ManualClockAudioOutputSink sink,
        OutputFlowId outputFlowId)
    {
        var events = new List<OutputPlaybackEvent>();
        await foreach (var playbackEvent in sink.ReadPlaybackEventsAsync(outputFlowId))
        {
            events.Add(playbackEvent);
        }

        return events;
    }
}

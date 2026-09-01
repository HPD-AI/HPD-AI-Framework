using System.Runtime.CompilerServices;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class ManagedAudioSessionOutputRouterV1Tests
{
    [Fact]
    public async Task Completion_keeps_route_alive_until_playback_observation_finishes()
    {
        var inner = new CompletingSink();
        IAudioOutputSink router = new ManagedAudioSessionOutputRouterV1(
            sessionId => sessionId == "session" ? inner : null);
        var flowId = new OutputFlowId("flow");
        var responseId = new ResponseId("response");
        var segmentId = new OutputSegmentId("segment");
        await router.StartAsync(new OutputAudioStream
        {
            SessionId = "session",
            OutputFlowId = flowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = true,
            MediaType = "audio/pcm",
            PayloadKind = OutputAudioPayloadKind.DecodedPcmFrame
        });
        await router.CompleteAsync(new OutputAudioStreamCompletion
        {
            OutputFlowId = flowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Disposition = OutputAudioStreamDisposition.Completed,
            CompletedAt = DateTimeOffset.UtcNow
        });

        var events = new List<OutputPlaybackEvent>();
        await foreach (var playbackEvent in router.ReadPlaybackEventsAsync(flowId))
            events.Add(playbackEvent);

        Assert.Single(events);
        Assert.IsType<OutputPlaybackCompletedEvent>(events[0]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await router.FlushAsync(flowId));
    }

    private sealed class CompletingSink : IAudioOutputSink
    {
        private OutputAudioStreamCompletion? _completion;

        public ValueTask<OutputSinkStartResult> StartAsync(
            OutputAudioStream stream,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OutputSinkStartResult
            {
                OutputFlowId = stream.OutputFlowId,
                ResponseId = stream.ResponseId,
                SegmentId = stream.SegmentId,
                SegmentIndex = stream.SegmentIndex,
                Disposition = OutputSinkStartDisposition.Accepted
            });

        public ValueTask WriteAsync(OutputAudioChunk chunk, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteAsync(
            OutputAudioStreamCompletion completion,
            CancellationToken cancellationToken = default)
        {
            _completion = completion;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
            OutputFlowId outputFlowId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var completion = Assert.IsType<OutputAudioStreamCompletion>(_completion);
            yield return new OutputPlaybackCompletedEvent
            {
                OutputFlowId = completion.OutputFlowId,
                ResponseId = completion.ResponseId,
                SegmentId = completion.SegmentId,
                SegmentIndex = completion.SegmentIndex,
                Cursor = new OutputPlaybackCursor
                {
                    OutputFlowId = completion.OutputFlowId,
                    ResponseId = completion.ResponseId,
                    SegmentId = completion.SegmentId,
                    SegmentIndex = completion.SegmentIndex
                }
            };
        }

        public ValueTask<OutputPlaybackBoundary> InterruptAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask FlushAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

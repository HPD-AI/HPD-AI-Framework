using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.LiveKit;

internal sealed class LiveKitAudioOutputSinkAdapter : IAudioOutputSink
{
    private readonly LiveKitOutboundAudioSink _sink;
    private readonly ConcurrentDictionary<OutputFlowId, Flow> _flows = [];

    internal LiveKitAudioOutputSinkAdapter(LiveKitOutboundAudioSink sink) => _sink = sink;

    public ValueTask<OutputSinkStartResult> StartAsync(OutputAudioStream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var flow = _flows.GetOrAdd(stream.OutputFlowId, static _ => new());
        flow.ResponseId = stream.ResponseId;
        flow.SegmentId = stream.SegmentId;
        flow.SegmentIndex = stream.SegmentIndex;
        flow.SourceTextEnd = checked(stream.SourceTextStart + stream.SourceTextLength);
        flow.IsFinalSegment = stream.IsFinalSegment;
        flow.Events.Writer.TryWrite(new OutputPlaybackQueuedEvent
        {
            OutputFlowId = stream.OutputFlowId,
            ResponseId = stream.ResponseId,
            SegmentId = stream.SegmentId,
            SegmentIndex = stream.SegmentIndex,
            ObservedAt = DateTimeOffset.UtcNow
        });
        return ValueTask.FromResult(new OutputSinkStartResult
        {
            OutputFlowId = stream.OutputFlowId,
            ResponseId = stream.ResponseId,
            SegmentId = stream.SegmentId,
            SegmentIndex = stream.SegmentIndex,
            Disposition = OutputSinkStartDisposition.Accepted
        });
    }

    public ValueTask WriteAsync(OutputAudioChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return chunk.Payload is DecodedOutputAudioFrame decoded
            ? _sink.WriteAsync(decoded.Frame, cancellationToken)
            : ValueTask.FromException(new NotSupportedException("LiveKit output accepts decoded PCM only."));
    }

    public ValueTask CompleteAsync(OutputAudioStreamCompletion completion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_flows.TryGetValue(completion.OutputFlowId, out var flow))
            return ValueTask.CompletedTask;

        flow.Events.Writer.TryWrite(new OutputPlaybackCompletedEvent
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
                SegmentIndex = completion.SegmentIndex,
                PlayedDuration = completion.Duration ?? TimeSpan.Zero,
                PlayedTextLength = flow.SourceTextEnd,
                Precision = OutputAlignmentPrecision.Approximate,
                ObservedAt = completion.CompletedAt
            },
            ObservedAt = completion.CompletedAt
        });
        if (flow.IsFinalSegment)
            flow.Events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
        OutputFlowId outputFlowId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var flow = _flows.GetOrAdd(outputFlowId, static _ => new());
        try
        {
            await foreach (var item in flow.Events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally { _flows.TryRemove(outputFlowId, out _); }
    }

    public async ValueTask<OutputPlaybackBoundary> InterruptAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default)
    {
        await _sink.FlushAsync(cancellationToken).ConfigureAwait(false);
        var boundary = Boundary(outputFlowId);
        if (_flows.TryGetValue(outputFlowId, out var flow))
        {
            flow.Events.Writer.TryWrite(new OutputPlaybackInterruptedEvent
            {
                OutputFlowId = outputFlowId,
                ResponseId = boundary.ResponseId,
                SegmentId = boundary.SegmentId ?? flow.SegmentId,
                SegmentIndex = boundary.SegmentIndex,
                Boundary = boundary,
                ObservedAt = boundary.ObservedAt
            });
            flow.Events.Writer.TryComplete();
        }
        return boundary;
    }

    public async ValueTask FlushAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
        await _sink.FlushAsync(cancellationToken).ConfigureAwait(false);

    private OutputPlaybackBoundary Boundary(OutputFlowId outputFlowId)
    {
        var flow = _flows.GetOrAdd(outputFlowId, static _ => new());
        return new()
        {
            OutputFlowId = outputFlowId,
            ResponseId = flow.ResponseId,
            SegmentId = flow.SegmentId,
            SegmentIndex = flow.SegmentIndex,
            PlayedDuration = TimeSpan.Zero,
            PlayedTextLength = 0,
            Precision = OutputAlignmentPrecision.Unknown,
            ObservedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class Flow
    {
        internal readonly Channel<OutputPlaybackEvent> Events = Channel.CreateBounded<OutputPlaybackEvent>(64);
        internal ResponseId ResponseId;
        internal OutputSegmentId SegmentId;
        internal int SegmentIndex;
        internal int SourceTextEnd;
        internal bool IsFinalSegment;
    }
}

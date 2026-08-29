using System.Collections.Concurrent;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio;

/// <summary>Routes one output flow to the live session selected at stream start.</summary>
internal sealed class ManagedAudioSessionOutputRouterV1(
    Func<string, IAudioOutputSink?> resolve) : IAudioOutputSink
{
    private readonly ConcurrentDictionary<OutputFlowId, IAudioOutputSink> _flows = [];

    public async ValueTask<OutputSinkStartResult> StartAsync(
        OutputAudioStream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (string.IsNullOrWhiteSpace(stream.SessionId) || resolve(stream.SessionId) is not { } sink)
        {
            return new OutputSinkStartResult
            {
                OutputFlowId = stream.OutputFlowId,
                ResponseId = stream.ResponseId,
                SegmentId = stream.SegmentId,
                SegmentIndex = stream.SegmentIndex,
                Disposition = OutputSinkStartDisposition.Rejected
            };
        }
        if (!_flows.TryAdd(stream.OutputFlowId, sink))
            throw new InvalidOperationException("An output flow is already routed to a live Audio session.");
        var result = await sink.StartAsync(stream, cancellationToken).ConfigureAwait(false);
        if (result.Disposition != OutputSinkStartDisposition.Accepted)
            _flows.TryRemove(stream.OutputFlowId, out _);
        return result;
    }

    public ValueTask WriteAsync(OutputAudioChunk chunk, CancellationToken cancellationToken = default) =>
        Sink(chunk.OutputFlowId).WriteAsync(chunk, cancellationToken);

    public async ValueTask CompleteAsync(
        OutputAudioStreamCompletion completion,
        CancellationToken cancellationToken = default)
    {
        var sink = Sink(completion.OutputFlowId);
        try { await sink.CompleteAsync(completion, cancellationToken).ConfigureAwait(false); }
        finally { _flows.TryRemove(completion.OutputFlowId, out _); }
    }

    public IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
        OutputFlowId outputFlowId,
        CancellationToken cancellationToken = default) =>
        Sink(outputFlowId).ReadPlaybackEventsAsync(outputFlowId, cancellationToken);

    public ValueTask<OutputPlaybackBoundary> InterruptAsync(
        OutputFlowId outputFlowId,
        CancellationToken cancellationToken = default) =>
        Sink(outputFlowId).InterruptAsync(outputFlowId, cancellationToken);

    public ValueTask FlushAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
        Sink(outputFlowId).FlushAsync(outputFlowId, cancellationToken);

    private IAudioOutputSink Sink(OutputFlowId outputFlowId) =>
        _flows.TryGetValue(outputFlowId, out var sink)
            ? sink
            : throw new InvalidOperationException("The output flow is not routed to a live Audio session.");
}

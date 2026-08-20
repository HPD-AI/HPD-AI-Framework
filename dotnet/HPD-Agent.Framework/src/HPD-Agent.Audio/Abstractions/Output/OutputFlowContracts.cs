namespace HPD.Agent.Audio.Output;

public interface IAudioOutputSink
{
    ValueTask<OutputSinkStartResult> StartAsync(
        OutputAudioStream stream,
        CancellationToken cancellationToken = default);

    ValueTask WriteAsync(
        OutputAudioChunk chunk,
        CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(
        OutputAudioStreamCompletion completion,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
        OutputFlowId outputFlowId,
        CancellationToken cancellationToken = default);

    ValueTask<OutputPlaybackBoundary> InterruptAsync(
        OutputFlowId outputFlowId,
        CancellationToken cancellationToken = default);

    ValueTask FlushAsync(
        OutputFlowId outputFlowId,
        CancellationToken cancellationToken = default);
}

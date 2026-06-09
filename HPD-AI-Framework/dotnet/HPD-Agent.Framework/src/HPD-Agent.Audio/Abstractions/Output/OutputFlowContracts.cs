namespace HPD.Agent.Audio.Output;

public interface IOutputFlow
{
    OutputFlowId Id { get; }

    OutputFlowSnapshot Snapshot { get; }

    ValueTask AppendTextAsync(
        ResponseId responseId,
        string text,
        bool isFinal,
        CancellationToken cancellationToken = default);

    ValueTask StartAudioStreamAsync(
        OutputAudioStream stream,
        CancellationToken cancellationToken = default);

    ValueTask AppendAudioChunkAsync(
        OutputAudioChunk chunk,
        CancellationToken cancellationToken = default);

    ValueTask CompleteAudioStreamAsync(
        OutputAudioStreamCompletion completion,
        CancellationToken cancellationToken = default);

    ValueTask AttachAudioArtifactAsync(
        OutputAudioArtifact artifact,
        CancellationToken cancellationToken = default);

    ValueTask MarkQueuedAsync(
        OutputPlaybackRequest request,
        CancellationToken cancellationToken = default);

    ValueTask MarkPlaybackStartedAsync(
        OutputPlaybackEvent playbackStarted,
        CancellationToken cancellationToken = default);

    ValueTask UpdatePlaybackProgressAsync(
        OutputPlaybackCursor cursor,
        CancellationToken cancellationToken = default);

    ValueTask<OutputCommitRecord> CompletePlayedAsync(
        OutputPlaybackCursor cursor,
        CancellationToken cancellationToken = default);

    ValueTask<OutputCommitRecord> CompleteSynthesizedNotPlayedAsync(
        CancellationToken cancellationToken = default);

    ValueTask<OutputCommitRecord> CompleteTextOnlyAsync(
        string reason,
        CancellationToken cancellationToken = default);

    ValueTask<OutputCommitRecord> CommitInterruptedAsync(
        OutputPlaybackBoundary boundary,
        CancellationToken cancellationToken = default);

    ValueTask<OutputCommitRecord> CommitQueuedUnplayedAsync(
        OutputPlaybackBoundary boundary,
        CancellationToken cancellationToken = default);

    ValueTask<OutputCommitRecord> FailPlaybackAsync(
        OutputPlaybackFailure failure,
        CancellationToken cancellationToken = default);
}

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

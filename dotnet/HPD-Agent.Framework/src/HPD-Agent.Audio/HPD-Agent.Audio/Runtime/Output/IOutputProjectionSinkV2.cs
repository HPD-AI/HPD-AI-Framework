using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.Runtime.Output;

internal interface IOutputProjectionSinkV2
{
    OutputFlowId Id { get; }
    OutputFlowSnapshot Snapshot { get; }
    ValueTask AppendTextAsync(ResponseId responseId,string text,bool isFinal,CancellationToken cancellationToken=default);
    ValueTask StartAudioStreamAsync(OutputAudioStream stream,CancellationToken cancellationToken=default);
    ValueTask AppendAudioChunkAsync(OutputAudioChunk chunk,CancellationToken cancellationToken=default);
    ValueTask CompleteAudioStreamAsync(OutputAudioStreamCompletion completion,CancellationToken cancellationToken=default);
    ValueTask AttachAudioArtifactAsync(OutputAudioArtifact artifact,CancellationToken cancellationToken=default);
    ValueTask MarkQueuedAsync(OutputPlaybackRequest request,CancellationToken cancellationToken=default);
    ValueTask MarkPlaybackStartedAsync(OutputPlaybackEvent playbackStarted,CancellationToken cancellationToken=default);
    ValueTask UpdatePlaybackProgressAsync(OutputPlaybackCursor cursor,CancellationToken cancellationToken=default);
    ValueTask<OutputCommitRecord> CompletePlayedAsync(OutputPlaybackCursor cursor,CancellationToken cancellationToken=default);
    ValueTask<OutputCommitRecord> CompleteSynthesizedNotPlayedAsync(CancellationToken cancellationToken=default);
    ValueTask<OutputCommitRecord> CompleteTextOnlyAsync(string reason,CancellationToken cancellationToken=default);
    ValueTask<OutputCommitRecord> CommitInterruptedAsync(OutputPlaybackBoundary boundary,CancellationToken cancellationToken=default);
    ValueTask<OutputCommitRecord> CommitQueuedUnplayedAsync(OutputPlaybackBoundary boundary,CancellationToken cancellationToken=default);
    ValueTask<OutputCommitRecord> FailPlaybackAsync(OutputPlaybackFailure failure,CancellationToken cancellationToken=default);
}

using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime;
using HPD.Agent.Audio.Runtime.Output;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class OutputFlowPlaybackTruthTests
{
    [Fact]
    public async Task MarkQueuedAsync_DoesNotClaimStartedOrPlayedTruth()
    {
        var ids = new RuntimeIdFactory();
        var flow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        const string text = "Queued speech is not heard yet.";

        await flow.AppendTextAsync(responseId, text, isFinal: true);
        var request = CreatePlaybackRequest(flow.Id, responseId, ids.NextOutputSegmentId(), text.Length);

        await flow.MarkQueuedAsync(request);

        Assert.Equal(OutputFlowState.Queued, flow.Snapshot.State);
        Assert.Equal(text, flow.Snapshot.Text);
        Assert.Null(flow.Snapshot.PlaybackBoundary);
        Assert.Empty(flow.Snapshot.AudioArtifacts);
    }

    [Fact]
    public async Task PlaybackProgress_StoresBoundaryWithoutCompletingOutput()
    {
        var ids = new RuntimeIdFactory();
        var flow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        const string text = "Only this prefix was heard before more audio queued.";
        var segmentId = ids.NextOutputSegmentId();

        await flow.AppendTextAsync(responseId, text, isFinal: true);
        await flow.MarkQueuedAsync(CreatePlaybackRequest(flow.Id, responseId, segmentId, text.Length));
        await flow.MarkPlaybackStartedAsync(CreatePlaybackStarted(flow.Id, responseId, segmentId));

        await flow.UpdatePlaybackProgressAsync(new OutputPlaybackCursor
        {
            OutputFlowId = flow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            PlayedDuration = TimeSpan.FromMilliseconds(450),
            PlayedTextLength = "Only this prefix".Length,
            Precision = OutputAlignmentPrecision.LocalOnly
        });

        Assert.Equal(OutputFlowState.Playing, flow.Snapshot.State);
        Assert.NotNull(flow.Snapshot.PlaybackBoundary);
        Assert.Equal("Only this prefix".Length, flow.Snapshot.PlaybackBoundary.PlayedTextLength);
        Assert.Equal(TimeSpan.FromMilliseconds(450), flow.Snapshot.PlaybackBoundary.PlayedDuration);
    }

    [Fact]
    public async Task CompletePlayedAsync_CommitsPlayedCompleteWithPlaybackBoundary()
    {
        var ids = new RuntimeIdFactory();
        var flow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        const string text = "The full synthesized response was played.";
        var segmentId = ids.NextOutputSegmentId();

        await flow.AppendTextAsync(responseId, text, isFinal: true);
        await flow.MarkQueuedAsync(CreatePlaybackRequest(flow.Id, responseId, segmentId, text.Length));
        await flow.MarkPlaybackStartedAsync(CreatePlaybackStarted(flow.Id, responseId, segmentId));

        var commit = await flow.CompletePlayedAsync(new OutputPlaybackCursor
        {
            OutputFlowId = flow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            PlayedDuration = TimeSpan.FromSeconds(2),
            PlayedTextLength = text.Length,
            Precision = OutputAlignmentPrecision.LocalOnly
        });

        Assert.Equal(OutputFlowState.PlayedComplete, flow.Snapshot.State);
        Assert.Equal(OutputCommitDisposition.PlayedComplete, commit.Disposition);
        Assert.Equal(text, commit.Text);
        Assert.NotNull(commit.PlaybackBoundary);
        Assert.Equal(text.Length, commit.PlaybackBoundary.PlayedTextLength);
    }

    [Fact]
    public async Task FailPlaybackAsync_CommitsPlaybackFailedWithoutClaimingPlayedBoundary()
    {
        var ids = new RuntimeIdFactory();
        var flow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        const string text = "This audio failed before playback truth.";
        var segmentId = ids.NextOutputSegmentId();

        await flow.AppendTextAsync(responseId, text, isFinal: true);
        await flow.MarkQueuedAsync(CreatePlaybackRequest(flow.Id, responseId, segmentId, text.Length));

        var commit = await flow.FailPlaybackAsync(new OutputPlaybackFailure
        {
            OutputFlowId = flow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Error = new AudioErrorInfo
            {
                Code = "sink_failed",
                Message = "The fake output sink failed.",
                Category = "playback"
            }
        });

        Assert.Equal(OutputFlowState.PlaybackFailed, flow.Snapshot.State);
        Assert.Equal(OutputCommitDisposition.PlaybackFailed, commit.Disposition);
        Assert.Equal(text, commit.Text);
        Assert.Null(commit.PlaybackBoundary);
        Assert.Equal("The fake output sink failed.", commit.Reason);
    }

    [Fact]
    public async Task CommitQueuedUnplayedAsync_CommitsZeroBoundaryWithoutClaimingHeardText()
    {
        var ids = new RuntimeIdFactory();
        var flow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        const string text = "This was queued but never played.";
        var segmentId = ids.NextOutputSegmentId();

        await flow.AppendTextAsync(responseId, text, isFinal: true);
        await flow.MarkQueuedAsync(CreatePlaybackRequest(flow.Id, responseId, segmentId, text.Length));

        var commit = await flow.CommitQueuedUnplayedAsync(new OutputPlaybackBoundary
        {
            OutputFlowId = flow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            PlayedDuration = TimeSpan.Zero,
            PlayedTextLength = 0,
            Precision = OutputAlignmentPrecision.LocalOnly
        });

        Assert.Equal(OutputFlowState.QueuedUnplayed, flow.Snapshot.State);
        Assert.Equal(OutputCommitDisposition.QueuedUnplayed, commit.Disposition);
        Assert.Equal(text, commit.Text);
        Assert.Equal(0, commit.PlaybackBoundary?.PlayedTextLength);
        Assert.Equal("Output was queued but cleared before playback.", commit.Reason);
    }

    private static OutputPlaybackRequest CreatePlaybackRequest(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId,
        int sourceTextLength)
    {
        return new OutputPlaybackRequest
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            EstimatedDuration = TimeSpan.FromSeconds(2),
            SourceTextStart = 0,
            SourceTextLength = sourceTextLength,
            MediaType = "audio/mpeg"
        };
    }

    private static OutputPlaybackStartedEvent CreatePlaybackStarted(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId)
    {
        return new OutputPlaybackStartedEvent
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0
        };
    }
}

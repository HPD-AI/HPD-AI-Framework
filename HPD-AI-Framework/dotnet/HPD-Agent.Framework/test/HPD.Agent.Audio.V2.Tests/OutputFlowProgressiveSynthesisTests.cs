using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime;
using HPD.Agent.Audio.Runtime.Output;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class OutputFlowProgressiveSynthesisTests
{
    [Fact]
    public async Task InMemoryOutputFlow_AppendsMultipleAudioStreamsAndArtifactsInOrder()
    {
        var ids = new RuntimeIdFactory();
        var flow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        var firstSegmentId = new OutputSegmentId("audio-0001");
        var secondSegmentId = new OutputSegmentId("audio-0002");

        await flow.AppendTextAsync(responseId, "First sentence. Second sentence.", isFinal: true);
        await AppendStreamAsync(flow, responseId, firstSegmentId, index: 0, isFinal: false, artifactId: "first.mp3");
        await AppendStreamAsync(flow, responseId, secondSegmentId, index: 1, isFinal: true, artifactId: "second.mp3");

        Assert.Equal(
            new[]
            {
                new OutputSegmentId($"{flow.Id.Value}:text-0001"),
                firstSegmentId,
                secondSegmentId
            },
            flow.Snapshot.SegmentIds);
        Assert.Equal([0, 1], flow.Snapshot.AudioStreams.Select(stream => stream.SegmentIndex));
        Assert.Equal([firstSegmentId, secondSegmentId], flow.Snapshot.AudioChunks.Select(chunk => chunk.SegmentId));
        Assert.Equal([firstSegmentId, secondSegmentId], flow.Snapshot.AudioArtifacts.Select(artifact => artifact.SegmentId));
        Assert.True(flow.Snapshot.AudioStreams[^1].IsFinalSegment);
    }

    [Fact]
    public async Task CompleteSynthesizedNotPlayedAsync_DoesNotClaimPlayedOrHeard()
    {
        var ids = new RuntimeIdFactory();
        var flow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        var firstSegmentId = new OutputSegmentId("audio-0001");
        var secondSegmentId = new OutputSegmentId("audio-0002");

        await flow.AppendTextAsync(responseId, "First sentence. Second sentence.", isFinal: true);
        await AppendStreamAsync(flow, responseId, firstSegmentId, index: 0, isFinal: false, artifactId: "first.mp3");
        await AppendStreamAsync(flow, responseId, secondSegmentId, index: 1, isFinal: true, artifactId: "second.mp3");

        var commit = await flow.CompleteSynthesizedNotPlayedAsync();

        Assert.Equal(OutputFlowState.SynthesizedNotPlayed, flow.Snapshot.State);
        Assert.Equal(OutputCommitDisposition.SynthesizedNotPlayed, commit.Disposition);
        Assert.Null(commit.PlaybackBoundary);
        Assert.Null(flow.Snapshot.PlaybackBoundary);
        Assert.Equal([firstSegmentId, secondSegmentId], commit.AudioStreams.Select(stream => stream.SegmentId));
        Assert.Equal([firstSegmentId, secondSegmentId], commit.AudioArtifacts.Select(artifact => artifact.SegmentId));
        Assert.Equal("Synthesized audio available; no playback sink observed.", commit.Reason);
    }

    private static async Task AppendStreamAsync(
        InMemoryOutputFlow flow,
        ResponseId responseId,
        OutputSegmentId segmentId,
        int index,
        bool isFinal,
        string artifactId)
    {
        var stream = new OutputAudioStream
        {
            OutputFlowId = flow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = index,
            IsFinalSegment = isFinal,
            SourceTextStart = index * 16,
            SourceTextLength = 15,
            ProviderKey = "fake-tts",
            ModelId = "fake-model",
            VoiceId = "voice-1",
            OutputFormat = "mp3",
            MediaType = "audio/mpeg",
            PayloadKind = OutputAudioPayloadKind.EncodedBytes,
            StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(index)
        };
        var artifact = new AudioArtifactRef(
            Store: "test-artifacts",
            ArtifactId: artifactId,
            MediaType: "audio/mpeg",
            SizeBytes: 12,
            Sha256: artifactId);

        await flow.StartAudioStreamAsync(stream);
        await flow.AppendAudioChunkAsync(new OutputAudioChunk
        {
            OutputFlowId = flow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = index,
            Sequence = 0,
            Payload = new EncodedOutputAudioData
            {
                ContentType = "audio/mpeg",
                Data = new byte[] { 1, 2, 3, 4 }
            },
            ObservedAt = DateTimeOffset.UnixEpoch.AddSeconds(index).AddMilliseconds(10),
            IsFinalChunk = true
        });
        await flow.CompleteAudioStreamAsync(new OutputAudioStreamCompletion
        {
            OutputFlowId = flow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = index,
            Disposition = OutputAudioStreamDisposition.Completed,
            ChunkCount = 1,
            SizeBytes = 4,
            CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(index).AddMilliseconds(20)
        });
        await flow.AttachAudioArtifactAsync(new OutputAudioArtifact
        {
            OutputFlowId = flow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = index,
            Artifact = artifact,
            MediaType = artifact.MediaType,
            SizeBytes = artifact.SizeBytes,
            Sha256 = artifact.Sha256,
            CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(index).AddMilliseconds(30)
        });
    }
}

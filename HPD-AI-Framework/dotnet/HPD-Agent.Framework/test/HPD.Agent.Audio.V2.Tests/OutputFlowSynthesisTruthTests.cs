using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Runtime;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Trace;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class OutputFlowSynthesisTruthTests
{
    [Fact]
    public async Task CompleteSynthesizedNotPlayedAsync_AttachesArtifactWithoutPlaybackBoundary()
    {
        var ids = new RuntimeIdFactory();
        var outputFlow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        const string assistantText = "Here is the spoken response.";
        var artifact = new AudioArtifactRef(
            Store: "test-artifacts",
            ArtifactId: "assistant-response.mp3",
            MediaType: "audio/mpeg",
            SizeBytes: 1234,
            Sha256: "abc123");
        const string streamMediaType = "audio/mpeg";
        const long streamSizeBytes = 1234;

        await outputFlow.AppendTextAsync(responseId, assistantText, isFinal: true);
        Assert.Equal(OutputFlowState.TextReady, outputFlow.Snapshot.State);

        var segmentId = ids.NextOutputSegmentId();
        var stream = new OutputAudioStream
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = true,
            SourceTextStart = 0,
            SourceTextLength = assistantText.Length,
            ProviderKey = "elevenlabs",
            ModelId = "eleven_turbo_v2_5",
            VoiceId = "voice-test",
            Language = "en",
            OutputFormat = "mp3_44100_128",
            MediaType = "audio/mpeg",
            PayloadKind = OutputAudioPayloadKind.EncodedBytes,
            StartedAt = DateTimeOffset.UnixEpoch
        };
        await outputFlow.StartAudioStreamAsync(stream);
        Assert.Equal(OutputFlowState.AudioStreaming, outputFlow.Snapshot.State);
        await outputFlow.AppendAudioChunkAsync(new OutputAudioChunk
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Sequence = 0,
            Payload = new EncodedOutputAudioData
            {
                ContentType = "audio/mpeg",
                Data = new byte[] { 1, 2, 3, 4 }
            },
            ObservedAt = DateTimeOffset.UnixEpoch.AddMilliseconds(10),
            IsFinalChunk = true
        });
        await outputFlow.CompleteAudioStreamAsync(new OutputAudioStreamCompletion
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Disposition = OutputAudioStreamDisposition.Completed,
            ChunkCount = 1,
            SizeBytes = 4,
            Duration = TimeSpan.FromSeconds(2),
            CompletedAt = DateTimeOffset.UnixEpoch.AddMilliseconds(20)
        });
        await outputFlow.AttachAudioArtifactAsync(new OutputAudioArtifact
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Artifact = artifact,
            MediaType = streamMediaType,
            SizeBytes = streamSizeBytes,
            Sha256 = artifact.Sha256,
            Duration = TimeSpan.FromSeconds(2),
            CapturedAt = DateTimeOffset.UnixEpoch.AddMilliseconds(30)
        });
        Assert.Equal(OutputFlowState.ArtifactCaptured, outputFlow.Snapshot.State);

        var commit = await outputFlow.CompleteSynthesizedNotPlayedAsync();

        Assert.Equal(OutputFlowState.SynthesizedNotPlayed, outputFlow.Snapshot.State);
        Assert.Equal(OutputCommitDisposition.SynthesizedNotPlayed, commit.Disposition);
        Assert.Equal(assistantText, commit.Text);
        Assert.Null(commit.PlaybackBoundary);
        Assert.Null(outputFlow.Snapshot.PlaybackBoundary);
        Assert.Equal(stream, commit.AudioStreams.Single());
        Assert.Equal(artifact, outputFlow.Snapshot.AudioArtifacts.Single().Artifact);
    }

    [Fact]
    public async Task CompleteTextOnlyAsync_CompletesWithoutSynthesisOrPlayback()
    {
        var ids = new RuntimeIdFactory();
        var outputFlow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        const string assistantText = "Text-only response.";

        await outputFlow.AppendTextAsync(responseId, assistantText, isFinal: true);
        var commit = await outputFlow.CompleteTextOnlyAsync("TTS disabled for this run.");

        Assert.Equal(OutputFlowState.TextOnlyCompleted, outputFlow.Snapshot.State);
        Assert.Equal(OutputCommitDisposition.TextOnly, commit.Disposition);
        Assert.Equal(assistantText, commit.Text);
        Assert.Equal("TTS disabled for this run.", commit.Reason);
        Assert.Empty(commit.AudioStreams);
        Assert.Empty(commit.AudioArtifacts);
        Assert.Null(commit.PlaybackBoundary);
        Assert.Empty(outputFlow.Snapshot.AudioStreams);
        Assert.Empty(outputFlow.Snapshot.AudioArtifacts);
    }

    [Theory]
    [InlineData(OutputAudioStreamDisposition.Canceled, OutputFlowState.Canceled)]
    [InlineData(OutputAudioStreamDisposition.Interrupted, OutputFlowState.Interrupted)]
    [InlineData(OutputAudioStreamDisposition.Failed, OutputFlowState.Failed)]
    public async Task CompleteAudioStreamAsync_PreservesTerminalStreamDisposition(
        OutputAudioStreamDisposition streamDisposition,
        OutputFlowState expectedState)
    {
        var ids = new RuntimeIdFactory();
        var outputFlow = new InMemoryOutputFlow(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        var segmentId = ids.NextOutputSegmentId();

        await outputFlow.AppendTextAsync(responseId, "Spoken text.", isFinal: true);
        await outputFlow.StartAudioStreamAsync(new OutputAudioStream
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = true,
            SourceTextStart = 0,
            SourceTextLength = "Spoken text.".Length,
            MediaType = "audio/mpeg",
            PayloadKind = OutputAudioPayloadKind.EncodedBytes,
            StartedAt = DateTimeOffset.UnixEpoch
        });

        await outputFlow.CompleteAudioStreamAsync(new OutputAudioStreamCompletion
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Disposition = streamDisposition,
            ChunkCount = 0,
            SizeBytes = 0,
            CompletedAt = DateTimeOffset.UnixEpoch,
            Error = new AudioErrorInfo
            {
                Code = streamDisposition.ToString(),
                Message = streamDisposition.ToString(),
                Category = "TextToSpeech"
            }
        });

        Assert.Equal(expectedState, outputFlow.Snapshot.State);
    }

    [Fact]
    public void TtsLedgerAndTraceRecords_CarrySynthesisMetadataWhileArtifactCaptureCarriesArtifactRef()
    {
        var ids = new RuntimeIdFactory();
        var clock = new RuntimeClock();
        var sessionId = new AudioSessionId("session-output-synthesis");
        var outputFlowId = ids.NextOutputFlowId();
        var responseId = ids.NextResponseId();
        var artifact = new AudioArtifactRef(
            Store: "test-artifacts",
            ArtifactId: "assistant-response.mp3",
            MediaType: "audio/mpeg",
            SizeBytes: 1234,
            Sha256: "abc123");
        const string streamMediaType = "audio/mpeg";
        const long streamSizeBytes = 1234;

        var requested = new TtsSynthesisRequestedLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.TtsSynthesis,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Text = "Speak this.",
            ProviderKey = "elevenlabs",
            ModelId = "eleven_turbo_v2_5",
            VoiceId = "voice-test",
            Language = "en",
            OutputFormat = "mp3_44100_128",
            ContentType = "audio/mpeg"
        };
        var result = new TtsSynthesisResultLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.TtsSynthesis,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Disposition = TtsSynthesisDisposition.Synthesized,
            MediaType = streamMediaType,
            SizeBytes = streamSizeBytes,
            Duration = TimeSpan.FromSeconds(2)
        };
        var artifactRecord = new OutputArtifactLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.OutputArtifact,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Artifact = artifact,
            Kind = OutputArtifactKind.SynthesizedAudio
        };
        var trace = new AudioOutputArtifactTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.OutputArtifact,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Artifact = artifact,
            Kind = OutputArtifactKind.SynthesizedAudio,
            MediaType = artifact.MediaType,
            SizeBytes = artifact.SizeBytes,
            Sha256 = artifact.Sha256
        };

        Assert.Equal("elevenlabs", requested.ProviderKey);
        Assert.Equal(TtsSynthesisDisposition.Synthesized, result.Disposition);
        Assert.Equal(OutputArtifactKind.SynthesizedAudio, artifactRecord.Kind);
        Assert.Equal(artifact, trace.Artifact);
    }
}

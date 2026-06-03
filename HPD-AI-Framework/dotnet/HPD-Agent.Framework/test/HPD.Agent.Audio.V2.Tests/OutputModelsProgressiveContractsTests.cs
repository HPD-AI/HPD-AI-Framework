using HPD.Agent.Audio;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime;
using HPD.Agent.Audio.Trace;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class OutputModelsProgressiveContractsTests
{
    [Fact]
    public void OutputFlowContract_ExposesStreamChunkAndPlaybackTruthMethods()
    {
        var methodNames = typeof(IOutputFlow)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains(nameof(IOutputFlow.StartAudioStreamAsync), methodNames);
        Assert.Contains(nameof(IOutputFlow.AppendAudioChunkAsync), methodNames);
        Assert.Contains(nameof(IOutputFlow.CompleteAudioStreamAsync), methodNames);
        Assert.Contains(nameof(IOutputFlow.AttachAudioArtifactAsync), methodNames);
        Assert.Contains(nameof(IOutputFlow.MarkQueuedAsync), methodNames);
        Assert.Contains(nameof(IOutputFlow.CompletePlayedAsync), methodNames);
        Assert.DoesNotContain("EnqueueAsync", methodNames);
    }

    [Fact]
    public void AudioOutputSinkContract_ConsumesStreamsAndChunks()
    {
        var methodNames = typeof(IAudioOutputSink)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains(nameof(IAudioOutputSink.StartAsync), methodNames);
        Assert.Contains(nameof(IAudioOutputSink.WriteAsync), methodNames);
        Assert.Contains(nameof(IAudioOutputSink.CompleteAsync), methodNames);
        Assert.Contains(nameof(IAudioOutputSink.ReadPlaybackEventsAsync), methodNames);
        Assert.DoesNotContain("EnqueueAsync", methodNames);
    }

    [Fact]
    public void OutputPlaybackRequest_DoesNotCarryArtifactIdentity()
    {
        var propertyNames = typeof(OutputPlaybackRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Artifact", propertyNames);
    }

    [Fact]
    public void OutputFlowState_UsesL708StreamAndPlayoutVocabulary()
    {
        Assert.Equal(0, (int)OutputFlowState.Created);
        Assert.Equal(1, (int)OutputFlowState.GeneratingText);
        Assert.Equal(2, (int)OutputFlowState.TextReady);
        Assert.Equal(3, (int)OutputFlowState.SynthesizingAudio);
        Assert.Equal(4, (int)OutputFlowState.AudioStreaming);
        Assert.Equal(5, (int)OutputFlowState.AudioStreamCompleted);
        Assert.Equal(6, (int)OutputFlowState.ArtifactCaptured);
        Assert.Equal(7, (int)OutputFlowState.Queued);
        Assert.Equal(8, (int)OutputFlowState.Playing);
        Assert.Equal(9, (int)OutputFlowState.PlayedPartial);
        Assert.Equal(10, (int)OutputFlowState.PlayedComplete);
        Assert.Equal(11, (int)OutputFlowState.Paused);
        Assert.Equal(12, (int)OutputFlowState.Interrupted);
        Assert.Equal(13, (int)OutputFlowState.Truncated);
        Assert.Equal(14, (int)OutputFlowState.Canceled);
        Assert.Equal(15, (int)OutputFlowState.Failed);
        Assert.Equal(16, (int)OutputFlowState.TextOnlyCompleted);
        Assert.Equal(17, (int)OutputFlowState.SynthesizedNotPlayed);
        Assert.Equal(18, (int)OutputFlowState.QueuedUnplayed);
        Assert.Equal(19, (int)OutputFlowState.PlaybackFailed);
    }

    [Fact]
    public void InMemoryOutputFlow_CanBeUsedThroughUnifiedOutputFlowContract()
    {
        var flow = new HPD.Agent.Audio.Runtime.Output.InMemoryOutputFlow(new OutputFlowId("output-contract"));

        Assert.IsAssignableFrom<IOutputFlow>(flow);
    }

    [Fact]
    public void OutputAudioChunk_DecodedPayloadUsesRealtimeMediaAudioFrame()
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
            SamplesPerChannel = 160
        };

        var payload = new DecodedOutputAudioFrame { Frame = frame };

        Assert.Equal(OutputAudioPayloadKind.DecodedPcmFrame, payload.Kind);
        Assert.Equal("audio/pcm", payload.MediaType);
        Assert.Equal(320, payload.SizeBytes);
        Assert.Equal(TimeSpan.FromMilliseconds(10), payload.Duration);
    }

    [Fact]
    public void TtsPacingContracts_DescribeStableProgressiveSegments()
    {
        var outputFlowId = new OutputFlowId("output-progressive");
        var responseId = new ResponseId("response-progressive");
        var segmentId = new OutputSegmentId("segment-0001");

        var context = new TextToSpeechPacingContext
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            GeneratedTextLength = 18,
            Options = new TextToSpeechPacingOptions
            {
                Mode = TextToSpeechPacingMode.Sentence,
                Continuation = new TextToSpeechContinuationOptions
                {
                    MaxCharacters = 240
                }
            }
        };
        var segment = new TextToSpeechSegment
        {
            SegmentId = segmentId,
            SegmentIndex = 0,
            Text = "Hello there.",
            IsFinalSegment = false,
            Kind = TextToSpeechSegmentKind.Sentence,
            SourceTextStart = 0,
            SourceTextLength = 12
        };
        var capability = new TextToSpeechCapabilityProfile
        {
            SupportsCompletedTextSynthesis = true,
            SupportsCompletedTextAudioStreaming = true,
            SupportsPushTextAudioStreaming = false,
            SupportsAlignment = true,
            PreferredStreamingFormats = ["pcm_16000"]
        };

        Assert.Equal(outputFlowId, context.OutputFlowId);
        Assert.Equal(responseId, context.ResponseId);
        Assert.Equal(segmentId, segment.SegmentId);
        Assert.Equal(0, segment.SegmentIndex);
        Assert.False(segment.IsFinalSegment);
        Assert.False(capability.SupportsPushTextAudioStreaming);
        Assert.True(capability.SupportsCompletedTextAudioStreaming);
        Assert.Equal(["pcm_16000"], capability.PreferredStreamingFormats);
    }

    [Fact]
    public void OutputStreamModels_CarryOrderedChunksAndArtifactsWithoutPlaybackTruth()
    {
        var responseId = new ResponseId("response-progressive");
        var firstSegmentId = new OutputSegmentId("segment-0001");
        var finalSegmentId = new OutputSegmentId("segment-0002");
        var firstArtifact = new AudioArtifactRef(
            Store: "hpd-content",
            ArtifactId: "session/artifacts/segment-0001.mp3",
            MediaType: "audio/mpeg",
            SizeBytes: 100,
            Sha256: "sha-first");
        var finalArtifact = new AudioArtifactRef(
            Store: "hpd-content",
            ArtifactId: "session/artifacts/segment-0002.mp3",
            MediaType: "audio/mpeg",
            SizeBytes: 200,
            Sha256: "sha-final");

        var streams = new[]
        {
            new OutputAudioStream
            {
                OutputFlowId = new OutputFlowId("output-progressive"),
                ResponseId = responseId,
                SegmentId = firstSegmentId,
                SegmentIndex = 0,
                IsFinalSegment = false,
                SourceTextStart = 0,
                SourceTextLength = 12,
                ProviderKey = "meai",
                MediaType = "audio/mpeg",
                PayloadKind = OutputAudioPayloadKind.EncodedBytes,
                StartedAt = DateTimeOffset.UnixEpoch
            },
            new OutputAudioStream
            {
                OutputFlowId = new OutputFlowId("output-progressive"),
                ResponseId = responseId,
                SegmentId = finalSegmentId,
                SegmentIndex = 1,
                IsFinalSegment = true,
                SourceTextStart = 13,
                SourceTextLength = 12,
                ProviderKey = "meai",
                MediaType = "audio/mpeg",
                PayloadKind = OutputAudioPayloadKind.EncodedBytes,
                StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
                Alignment = new TextToSpeechAlignment
                {
                    Precision = OutputAlignmentPrecision.Approximate,
                    Spans =
                    [
                        new TextToSpeechAlignmentSpan
                        {
                            SourceTextStart = 13,
                            SourceTextLength = 12,
                            AudioStart = TimeSpan.Zero,
                            AudioDuration = TimeSpan.FromSeconds(1.2),
                            Text = "How are you?"
                        }
                    ]
                }
            }
        };
        var chunks = new[]
        {
            new OutputAudioChunkMetadata
            {
                OutputFlowId = new OutputFlowId("output-progressive"),
                ResponseId = responseId,
                SegmentId = firstSegmentId,
                SegmentIndex = 0,
                Sequence = 0,
                PayloadKind = OutputAudioPayloadKind.EncodedBytes,
                MediaType = firstArtifact.MediaType,
                SizeBytes = firstArtifact.SizeBytes ?? 0,
                ObservedAt = DateTimeOffset.UnixEpoch,
                IsFinalChunk = true
            },
            new OutputAudioChunkMetadata
            {
                OutputFlowId = new OutputFlowId("output-progressive"),
                ResponseId = responseId,
                SegmentId = finalSegmentId,
                SegmentIndex = 1,
                Sequence = 0,
                PayloadKind = OutputAudioPayloadKind.EncodedBytes,
                MediaType = finalArtifact.MediaType,
                SizeBytes = finalArtifact.SizeBytes ?? 0,
                ObservedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
                IsFinalChunk = true
            }
        };
        var artifacts = new[]
        {
            new OutputAudioArtifact
            {
                OutputFlowId = new OutputFlowId("output-progressive"),
                ResponseId = responseId,
                SegmentId = firstSegmentId,
                SegmentIndex = 0,
                Artifact = firstArtifact,
                MediaType = firstArtifact.MediaType,
                SizeBytes = firstArtifact.SizeBytes,
                Sha256 = firstArtifact.Sha256,
                CapturedAt = DateTimeOffset.UnixEpoch.AddMilliseconds(20)
            },
            new OutputAudioArtifact
            {
                OutputFlowId = new OutputFlowId("output-progressive"),
                ResponseId = responseId,
                SegmentId = finalSegmentId,
                SegmentIndex = 1,
                Artifact = finalArtifact,
                MediaType = finalArtifact.MediaType,
                SizeBytes = finalArtifact.SizeBytes,
                Sha256 = finalArtifact.Sha256,
                CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(1).AddMilliseconds(20)
            }
        };

        var snapshot = new OutputFlowSnapshot
        {
            Id = new OutputFlowId("output-progressive"),
            State = OutputFlowState.SynthesizedNotPlayed,
            ResponseId = responseId,
            Text = "Hello there. How are you?",
            AudioStreams = streams,
            AudioChunks = chunks,
            AudioArtifacts = artifacts
        };
        var commit = new OutputCommitRecord
        {
            OutputFlowId = snapshot.Id,
            ResponseId = responseId,
            Disposition = OutputCommitDisposition.SynthesizedNotPlayed,
            Text = snapshot.Text,
            AudioStreams = streams,
            AudioArtifacts = artifacts
        };

        Assert.Equal([0, 1], snapshot.AudioStreams.Select(stream => stream.SegmentIndex));
        Assert.Equal([firstSegmentId, finalSegmentId], snapshot.AudioChunks.Select(chunk => chunk.SegmentId));
        Assert.Equal([firstSegmentId, finalSegmentId], snapshot.AudioArtifacts.Select(artifact => artifact.SegmentId));
        Assert.True(snapshot.AudioStreams[^1].IsFinalSegment);
        Assert.Equal(OutputCommitDisposition.SynthesizedNotPlayed, commit.Disposition);
        Assert.Null(snapshot.PlaybackBoundary);
        Assert.Null(commit.PlaybackBoundary);
    }

    [Fact]
    public void ProgressiveTtsLedgerAndTraceRecords_CarrySegmentIdentityWithSeparateArtifactCapture()
    {
        var ids = new RuntimeIdFactory();
        var clock = new RuntimeClock();
        var sessionId = new AudioSessionId("session-progressive");
        var outputFlowId = ids.NextOutputFlowId();
        var responseId = ids.NextResponseId();
        var segmentId = ids.NextOutputSegmentId();
        var artifact = new AudioArtifactRef(
            Store: "hpd-content",
            ArtifactId: "session-progressive/artifacts/segment-0001.mp3",
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
            Text = "Speak this segment.",
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = false,
            SourceTextStart = 4,
            SourceTextLength = 19,
            ProviderKey = "meai",
            ContentType = streamMediaType
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
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = false,
            SourceTextStart = 4,
            SourceTextLength = 19,
            MediaType = streamMediaType,
            SizeBytes = streamSizeBytes
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
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = false,
            SourceTextStart = 4,
            SourceTextLength = 19,
            Kind = OutputArtifactKind.SynthesizedAudio
        };
        var ttsTrace = new AudioTtsSynthesisTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.TtsSynthesis,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Disposition = TtsSynthesisDisposition.Synthesized,
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = false,
            SourceTextStart = 4,
            SourceTextLength = 19,
            ProviderKey = "meai",
            MediaType = streamMediaType,
            SizeBytes = streamSizeBytes
        };
        var artifactTrace = new AudioOutputArtifactTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.OutputArtifact,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Artifact = artifact,
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = false,
            SourceTextStart = 4,
            SourceTextLength = 19,
            Kind = OutputArtifactKind.SynthesizedAudio,
            MediaType = artifact.MediaType,
            SizeBytes = artifact.SizeBytes,
            Sha256 = artifact.Sha256
        };

        Assert.Equal(segmentId, requested.SegmentId);
        Assert.Equal(segmentId, result.SegmentId);
        Assert.Equal(segmentId, artifactRecord.SegmentId);
        Assert.Equal(segmentId, ttsTrace.SegmentId);
        Assert.Equal(segmentId, artifactTrace.SegmentId);
        Assert.Equal(4, result.SourceTextStart);
        Assert.Equal(19, artifactTrace.SourceTextLength);
        Assert.Equal(artifact, artifactRecord.Artifact);
    }
}

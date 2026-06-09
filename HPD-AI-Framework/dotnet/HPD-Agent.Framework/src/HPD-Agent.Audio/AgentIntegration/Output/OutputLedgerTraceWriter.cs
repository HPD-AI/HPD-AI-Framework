using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Trace;

namespace HPD.Agent.Audio.AgentIntegration.Output;

internal sealed class OutputLedgerTraceWriter
{
    public void AppendAssistantOutput(
        List<RealtimeLedgerRecord> ledger,
        List<RealtimeAudioTraceRecord> trace,
        AudioSessionId sessionId,
        AudioCorrelation correlation,
        OutputFlowId outputFlowId,
        ResponseId responseId,
        string text,
        OutputDisposition disposition)
    {
        var record = new AssistantOutputLedgerRecord
        {
            Id = NextLedgerId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.AssistantOutput,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Text = text,
            Disposition = disposition
        };
        ledger.Add(record);
        trace.Add(new AudioLedgerTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.Ledger,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            LedgerRecordId = record.Id,
            LedgerFamily = record.Family
        });
        trace.Add(new AudioAssistantOutputTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.AssistantOutput,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Disposition = disposition,
            Text = text
        });
    }

    public void AppendTtsRequested(
        List<RealtimeLedgerRecord> ledger,
        List<RealtimeAudioTraceRecord> trace,
        AudioSessionId sessionId,
        AudioCorrelation correlation,
        OutputFlowId outputFlowId,
        TextToSpeechSegmentRequest request,
        string providerKey)
    {
        var record = new TtsSynthesisRequestedLedgerRecord
        {
            Id = NextLedgerId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.TtsSynthesis,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = request.ResponseId,
            Text = request.Text,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            IsFinalSegment = request.IsFinalSegment,
            SourceTextStart = request.SourceTextStart,
            SourceTextLength = request.SourceTextLength,
            ProviderKey = providerKey,
            ModelId = request.ModelId,
            VoiceId = request.VoiceId,
            Language = request.Language,
            OutputFormat = request.OutputFormat,
            ContentType = request.ContentType
        };
        ledger.Add(record);
        trace.Add(new AudioLedgerTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.Ledger,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            LedgerRecordId = record.Id,
            LedgerFamily = record.Family
        });
        trace.Add(new AudioTtsSynthesisTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.TtsSynthesis,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = request.ResponseId,
            Disposition = TtsSynthesisDisposition.Requested,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            IsFinalSegment = request.IsFinalSegment,
            SourceTextStart = request.SourceTextStart,
            SourceTextLength = request.SourceTextLength,
            ProviderKey = providerKey,
            ModelId = request.ModelId,
            VoiceId = request.VoiceId,
            Language = request.Language,
            OutputFormat = request.OutputFormat,
            MediaType = request.ContentType
        });
    }

    public void AppendTtsResult(
        List<RealtimeLedgerRecord> ledger,
        List<RealtimeAudioTraceRecord> trace,
        AudioSessionId sessionId,
        AudioCorrelation correlation,
        OutputFlowId outputFlowId,
        ResponseId responseId,
        TextToSpeechSegmentRequest request,
        string providerKey,
        string? modelId,
        AssistantTextToSpeechOutputOptions options,
        TtsSynthesisDisposition disposition,
        string? mediaType,
        long? sizeBytes,
        TimeSpan? duration,
        AudioErrorInfo? error,
        DateTimeOffset? providerFirstAudioAt = null)
    {
        var record = new TtsSynthesisResultLedgerRecord
        {
            Id = NextLedgerId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.TtsSynthesis,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Disposition = disposition,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            IsFinalSegment = request.IsFinalSegment,
            SourceTextStart = request.SourceTextStart,
            SourceTextLength = request.SourceTextLength,
            MediaType = mediaType,
            SizeBytes = sizeBytes,
            Duration = duration,
            Error = error
        };
        ledger.Add(record);
        trace.Add(new AudioLedgerTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.Ledger,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            LedgerRecordId = record.Id,
            LedgerFamily = record.Family
        });
        trace.Add(new AudioTtsSynthesisTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.TtsSynthesis,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Disposition = disposition,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            IsFinalSegment = request.IsFinalSegment,
            SourceTextStart = request.SourceTextStart,
            SourceTextLength = request.SourceTextLength,
            ProviderKey = providerKey,
            ModelId = modelId,
            VoiceId = options.VoiceId,
            Language = options.Language,
            OutputFormat = options.OutputFormat,
            MediaType = mediaType,
            SizeBytes = sizeBytes,
            Duration = duration,
            ProviderFirstAudioAt = providerFirstAudioAt,
            Error = error
        });
    }

    public void AppendOutputArtifact(
        List<RealtimeLedgerRecord> ledger,
        List<RealtimeAudioTraceRecord> trace,
        AudioSessionId sessionId,
        AudioCorrelation correlation,
        OutputFlowId outputFlowId,
        ResponseId responseId,
        TextToSpeechSegmentRequest request,
        StoredAudioArtifact artifact)
    {
        var record = new OutputArtifactLedgerRecord
        {
            Id = NextLedgerId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.OutputArtifact,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Artifact = artifact.Artifact,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            IsFinalSegment = request.IsFinalSegment,
            SourceTextStart = request.SourceTextStart,
            SourceTextLength = request.SourceTextLength,
            Kind = OutputArtifactKind.SynthesizedAudio,
            CaptureDisposition = MediaCaptureDisposition.ArtifactRef
        };
        ledger.Add(record);
        trace.Add(new AudioLedgerTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.Ledger,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            LedgerRecordId = record.Id,
            LedgerFamily = record.Family
        });
        trace.Add(new AudioOutputArtifactTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.OutputArtifact,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            Artifact = artifact.Artifact,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            IsFinalSegment = request.IsFinalSegment,
            SourceTextStart = request.SourceTextStart,
            SourceTextLength = request.SourceTextLength,
            Kind = OutputArtifactKind.SynthesizedAudio,
            CaptureDisposition = MediaCaptureDisposition.ArtifactRef,
            MediaType = artifact.MediaType,
            SizeBytes = artifact.SizeBytes,
            Sha256 = artifact.Sha256
        });
    }

    public void AppendOutputPlayback(
        List<RealtimeLedgerRecord> ledger,
        List<RealtimeAudioTraceRecord> trace,
        AudioSessionId sessionId,
        AudioCorrelation correlation,
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId? segmentId,
        int segmentIndex,
        OutputPlaybackDisposition disposition,
        TimeSpan playedDuration,
        int playedTextLength,
        OutputAlignmentPrecision precision,
        AudioErrorInfo? error)
    {
        var record = new OutputPlaybackLedgerRecord
        {
            Id = NextLedgerId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.OutputPlayback,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = segmentIndex,
            Disposition = disposition,
            PlayedDuration = playedDuration,
            PlayedTextLength = playedTextLength,
            Precision = precision,
            Error = error
        };
        ledger.Add(record);
        trace.Add(new AudioLedgerTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.Ledger,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            LedgerRecordId = record.Id,
            LedgerFamily = record.Family
        });
        trace.Add(new AudioOutputPlaybackTraceRecord
        {
            Id = NextTraceId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.OutputPlayback,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = segmentIndex,
            Disposition = disposition,
            PlayedDuration = playedDuration,
            PlayedTextLength = playedTextLength,
            Precision = precision,
            Error = error
        });
    }

    private static LedgerRecordId NextLedgerId() => new($"ledger-{Guid.NewGuid():N}");

    private static TraceRecordId NextTraceId() => new($"trace-{Guid.NewGuid():N}");
}

namespace HPD.Agent.Audio.Providers;

/// <summary>
/// Owns one retained provider-native speech-to-text session.
/// </summary>
internal interface IStreamingSpeechToTextParticipant : IAsyncDisposable
{
    StreamingSpeechToTextParticipantState State { get; }

    ulong ProviderSessionEpoch { get; }

    ValueTask<StreamingSpeechToTextReady> ConnectAsync(
        StreamingSpeechToTextConnectRequest request,
        CancellationToken cancellationToken = default);

    ValueTask WriteAudioAsync(
        StreamingSpeechToTextAudioChunk chunk,
        CancellationToken cancellationToken = default);

    ValueTask<StreamingSpeechToTextCommitDispatch> CommitAsync(
        StreamingSpeechToTextCommitRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamingSpeechToTextObservation> ReadObservationsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StreamingSpeechToTextUpdateDisposition> UpdateAsync(
        StreamingSpeechToTextUpdateRequest request,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

internal interface IStreamingSpeechToTextParticipantFactory
{
    StreamingSpeechToTextParticipantConfiguration Configuration { get; }

    ValueTask<IStreamingSpeechToTextParticipant> CreateAsync(
        CancellationToken cancellationToken = default);
}

internal sealed record StreamingSpeechToTextParticipantConfiguration
{
    public required string ProviderKey { get; init; }
    public required string ModelId { get; init; }
    public required StreamingSpeechToTextContributionSafety Safety { get; init; }
    public string? LanguageCode { get; init; }
    public IReadOnlyList<string> Keyterms { get; init; } = Array.Empty<string>();
    public bool IncludeTimestamps { get; init; } = true;
    public bool IncludeLanguageDetection { get; init; } = true;
}

[Flags]
internal enum StreamingSpeechToTextContributionSafety
{
    None = 0,
    RetainedLiveSession = 1,
    BoundedTelemetry = 2,
    PrivacySafeByDefault = 4,
    Complete = RetainedLiveSession | BoundedTelemetry | PrivacySafeByDefault
}

internal enum StreamingSpeechToTextParticipantState
{
    Created = 0,
    Connecting = 1,
    Ready = 2,
    Stopping = 3,
    Stopped = 4,
    Faulted = 5
}

internal enum StreamingSpeechToTextCommitStrategy
{
    Manual = 0,
    ProviderVoiceActivityDetection = 1
}

internal sealed record StreamingSpeechToTextConnectRequest
{
    public required string ModelId { get; init; }

    public required StreamingSpeechToTextAudioFormat AudioFormat { get; init; }

    public StreamingSpeechToTextCommitStrategy CommitStrategy { get; init; }

    public string? LanguageCode { get; init; }

    public string? PreviousText { get; init; }

    public bool IncludeTimestamps { get; init; } = true;

    public bool IncludeLanguageDetection { get; init; } = true;

    public IReadOnlyList<string> Keyterms { get; init; } = Array.Empty<string>();
}

internal sealed record StreamingSpeechToTextAudioFormat
{
    public required int SampleRateHz { get; init; }

    public required int ChannelCount { get; init; }

    public required int BitsPerSample { get; init; }

    public string Encoding { get; init; } = "pcm";
}

internal sealed class StreamingSpeechToTextAudioChunk
{
    private readonly byte[] _payload;

    public StreamingSpeechToTextAudioChunk(ulong sequence, ReadOnlySpan<byte> payload)
    {
        if (sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (payload.IsEmpty)
            throw new ArgumentException("An audio chunk cannot be empty.", nameof(payload));

        Sequence = sequence;
        _payload = payload.ToArray();
    }

    public ulong Sequence { get; }

    public ReadOnlyMemory<byte> Payload => _payload;
}

internal sealed record StreamingSpeechToTextReady
{
    public required ulong ProviderSessionEpoch { get; init; }

    public required string ProviderSessionId { get; init; }

    public required StreamingSpeechToTextAudioFormat EffectiveAudioFormat { get; init; }
}

internal sealed record StreamingSpeechToTextCommitRequest
{
    public required string OperationId { get; init; }
}

internal sealed record StreamingSpeechToTextCommitDispatch
{
    public required string OperationId { get; init; }

    public required ulong ProviderSessionEpoch { get; init; }

    public required ulong DispatchSequence { get; init; }

    public StreamingSpeechToTextCommitDispatchOutcome Outcome { get; init; } =
        StreamingSpeechToTextCommitDispatchOutcome.DispatchedOutcomeUnknown;
}

internal enum StreamingSpeechToTextCommitDispatchOutcome
{
    DispatchedOutcomeUnknown = 0
}

internal enum StreamingSpeechToTextObservationKind
{
    PartialTranscript = 0,
    FinalTranscript = 1,
    FinalTranscriptWithTimestamps = 2,
    CommittedTranscript = 3,
    CommittedTranscriptWithTimestamps = 4,
    Error = 5,
    Unknown = 6,
    SessionClosed = 7
}

internal sealed record StreamingSpeechToTextObservation
{
    public required ulong ProviderSessionEpoch { get; init; }

    public required ulong Sequence { get; init; }

    public required StreamingSpeechToTextObservationKind Kind { get; init; }

    public string? Text { get; init; }

    public string? LanguageCode { get; init; }

    public string? ProviderEventType { get; init; }

    public string? SafeCode { get; init; }

    public string? Detail { get; init; }

    public string? EvidenceSha256 { get; init; }

    public IReadOnlyList<StreamingSpeechToTextWordTiming> WordTimings { get; init; } =
        Array.Empty<StreamingSpeechToTextWordTiming>();
}

internal sealed record StreamingSpeechToTextWordTiming
{
    public required string Text { get; init; }

    public TimeSpan? Start { get; init; }

    public TimeSpan? End { get; init; }
}

internal sealed record StreamingSpeechToTextUpdateRequest
{
    public required string OperationId { get; init; }

    public required ReadOnlyMemory<byte> Fingerprint { get; init; }

    public string? LanguageCode { get; init; }

    public IReadOnlyList<string>? Keyterms { get; init; }
}

internal enum StreamingSpeechToTextUpdateDisposition
{
    Unchanged = 0,
    Applied = 1,
    ReconnectRequired = 2,
    Rejected = 3,
    OutcomeUnknown = 4
}

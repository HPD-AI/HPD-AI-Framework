namespace HPD.Agent.Audio.Output;

public interface IPushTextToSpeechStreamFactory
{
    ValueTask<IPushTextToSpeechStream> OpenStreamAsync(
        PushTextToSpeechStreamRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPushTextToSpeechStream : IAsyncDisposable
{
    ValueTask PushTextAsync(
        PushTextToSpeechInput input,
        CancellationToken cancellationToken = default);

    ValueTask CompleteInputAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<PushTextToSpeechAudioUpdate> ReadAudioAsync(
        CancellationToken cancellationToken = default);

    ValueTask CancelAsync(
        CancellationToken cancellationToken = default);
}

public sealed record PushTextToSpeechStreamRequest
{
    public required AudioSessionId SessionId { get; init; }

    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required string ProviderKey { get; init; }

    public string? ModelId { get; init; }

    public string? VoiceId { get; init; }

    public string? Language { get; init; }

    public string? OutputFormat { get; init; }

    public string? ContentType { get; init; }

    public float? Speed { get; init; }

    public TextToSpeechPacingOptions? PacingOptions { get; init; }

    public PushTextInputAggregationMode InputAggregationMode { get; init; } =
        PushTextInputAggregationMode.ProviderDefault;
}

public sealed record PushTextToSpeechInput
{
    public required ResponseId ResponseId { get; init; }

    public required string Text { get; init; }

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public bool IsFinalInput { get; init; }
}

public sealed record PushTextToSpeechAudioUpdate
{
    public required ReadOnlyMemory<byte> AudioData { get; init; }

    public string? MediaType { get; init; }

    public string? ModelId { get; init; }
}

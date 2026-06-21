namespace HPD.Agent.Audio.Output;

public sealed record TextToSpeechCapabilityProfile
{
    public bool SupportsCompletedTextSynthesis { get; init; } = true;

    public bool SupportsCompletedTextAudioStreaming { get; init; }

    public bool SupportsPushTextAudioStreaming { get; init; }

    public bool SupportsAlignment { get; init; }

    public bool SupportsCancellationBeforeAudio { get; init; } = true;

    public bool SupportsCancellationAfterAudio { get; init; }

    public IReadOnlyList<string> PreferredStreamingFormats { get; init; } = [];
}

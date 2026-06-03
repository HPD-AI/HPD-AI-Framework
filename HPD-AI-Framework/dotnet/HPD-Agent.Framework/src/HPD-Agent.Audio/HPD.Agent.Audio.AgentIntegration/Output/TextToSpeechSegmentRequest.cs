using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.AgentIntegration.Output;

internal sealed record TextToSpeechSegmentRequest
{
    public required ResponseId ResponseId { get; init; }

    public required string Text { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public bool IsFinalSegment { get; init; } = true;

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public string? ProviderKey { get; init; }

    public string? ModelId { get; init; }

    public string? VoiceId { get; init; }

    public string? Language { get; init; }

    public string? OutputFormat { get; init; }

    public string? ContentType { get; init; }
}

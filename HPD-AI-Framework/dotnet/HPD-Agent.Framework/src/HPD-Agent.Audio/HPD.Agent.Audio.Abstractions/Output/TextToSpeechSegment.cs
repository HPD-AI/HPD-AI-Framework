namespace HPD.Agent.Audio.Output;

public sealed record TextToSpeechSegment
{
    public required OutputSegmentId SegmentId { get; init; }

    public required string Text { get; init; }

    public string? SourceText { get; init; }

    public required int SegmentIndex { get; init; }

    public required bool IsFinalSegment { get; init; }

    public TextToSpeechSegmentKind Kind { get; init; } = TextToSpeechSegmentKind.Sentence;

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public string? SanitizerPolicyId { get; init; }
}

public enum TextToSpeechSegmentKind
{
    Sentence = 0,
    Phrase = 1,
    TokenBatch = 2,
    Remainder = 3,
    Explicit = 4
}

namespace HPD.Agent.Audio.Output;

public sealed record TextToSpeechPacingOptions
{
    public TextToSpeechPacingMode Mode { get; init; } = TextToSpeechPacingMode.Sentence;

    public TextToSpeechFirstSegmentOptions First { get; init; } = new();

    public TextToSpeechContinuationOptions Continuation { get; init; } = new();

    public TextToSpeechBoundaryOptions Boundaries { get; init; } = new();

    public TextToSpeechFilteringOptions Filtering { get; init; } = new();
}

public sealed record TextToSpeechFirstSegmentOptions
{
    public int MinCharacters { get; init; } = 24;

    public int MaxCharacters { get; init; } = 220;

    public bool EmitFirstSafeSentenceImmediately { get; init; } = true;
}

public sealed record TextToSpeechContinuationOptions
{
    public int MaxCharacters { get; init; } = 360;

    public bool PreferSentenceBoundaries { get; init; } = true;

    public bool AllowPhraseBoundaries { get; init; }

    public int MaxInFlightSynthesisRequests { get; init; } = 1;
}

public sealed record TextToSpeechBoundaryOptions
{
    public bool RequireLookahead { get; init; } = true;

    public bool ProtectDecimals { get; init; } = true;

    public bool ProtectAbbreviations { get; init; } = true;

    public bool ProtectInitials { get; init; } = true;

    public bool ProtectUrls { get; init; } = true;

    public bool ProtectEllipses { get; init; } = true;
}

public sealed record TextToSpeechFilteringOptions
{
    public bool Enabled { get; init; } = true;

    public bool RemoveCodeBlocks { get; init; } = true;

    public bool RemoveMarkdownTables { get; init; } = true;

    public bool StripMarkdownFormatting { get; init; } = true;

    public bool SimplifyLinks { get; init; } = true;

    public bool NormalizeWhitespace { get; init; } = true;

    public bool CollapseRepeatedPunctuation { get; init; } = true;

    public TextToSpeechEmojiPolicy EmojiPolicy { get; init; } = TextToSpeechEmojiPolicy.Remove;
}

public enum TextToSpeechEmojiPolicy
{
    Remove = 0,
    Keep = 1,
    Verbalize = 2
}

public enum TextToSpeechPacingMode
{
    Sentence = 0,
    Phrase = 1,
    TokenBatch = 2,
    Manual = 3
}

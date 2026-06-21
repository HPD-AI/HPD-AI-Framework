namespace HPD.Agent.Audio.Output;

public sealed record TextToSpeechPacingContext
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public TextToSpeechPacingOptions Options { get; init; } = new();

    public int GeneratedTextLength { get; init; }

    public bool IsFinalInput { get; init; }
}

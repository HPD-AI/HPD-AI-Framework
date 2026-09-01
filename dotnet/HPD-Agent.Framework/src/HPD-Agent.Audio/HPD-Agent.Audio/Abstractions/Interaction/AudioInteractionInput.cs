using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Interaction;

public abstract record AudioInteractionInput
{
    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public sealed record InteractionInputMedia(CanonicalMediaEnvelope Envelope) : AudioInteractionInput;

public sealed record InteractionInputText(string Text) : AudioInteractionInput;

public sealed record InteractionInputToolResult(string ToolCallId, ToolResultPayload Result) : AudioInteractionInput;

public sealed record InteractionInputControl(string Kind, AudioExtensionData Metadata) : AudioInteractionInput;

public static class RealtimeInteractionControlKinds
{
    public const string CreateResponse = "create-response";
}

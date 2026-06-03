using HPD.Agent;
using HPD.Agent.Audio.Output;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Output;

#pragma warning disable MEAI001

public sealed record AssistantTextToSpeechOutputOptions
{
    public required ITextToSpeechClient TextToSpeechClient { get; init; }

    public required IContentStore? ContentStore { get; init; }

    public AssistantAudioArtifactCapturePolicy ArtifactCapturePolicy { get; init; } =
        AssistantAudioArtifactCapturePolicy.ContentStoreArtifact;

    public string? ProviderKey { get; init; }

    public string? ModelId { get; init; }

    public string? VoiceId { get; init; }

    public string? Language { get; init; }

    public string? OutputFormat { get; init; }

    public string? ContentType { get; init; }

    public float? Speed { get; init; }
}

using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio;

#pragma warning disable MEAI001

public sealed class AudioRuntimeAttachmentOptions
{
    public bool Enabled { get; set; } = true;

    public bool RunAudioInteractionRuntime { get; set; } = true;

    public bool RunAudioInteractionRuntimeForRealtimeTransport { get; set; }

    public bool AnnotateAudioInputMetadata { get; set; } = true;

    public bool ProjectCommittedTranscriptsIntoUserMessage { get; set; } = true;

    public AudioPolicySet PolicySet { get; set; } = new();

    public IProviderRoute? ProviderRoute { get; set; }

    public Func<IInputContentSourceResolver, IProviderRoute>? ProviderRouteResolver { get; set; }

    public IReadOnlyList<ProviderCapabilityProfile> ProviderCandidates { get; set; } = [];

    public IAudioInteractionSessionFactory? InteractionSessionFactory { get; set; }

    public Func<IInputContentSourceResolver, IAudioInteractionSessionFactory>? InteractionSessionFactoryResolver { get; set; }

    public IThreadProjectionSink? ThreadProjectionSink { get; set; }

    public AssistantOutputSynthesisMode AssistantOutputSynthesisMode { get; set; }

    public TextToSpeechPacingOptions AssistantOutputPacingOptions { get; set; } = new();

    public ProgressiveTextToSpeechRouteMode AssistantOutputProgressiveRouteMode { get; set; }

    public PushTextInputAggregationMode AssistantOutputPushTextAggregationMode { get; set; } =
        PushTextInputAggregationMode.ProviderDefault;

    public AssistantAudioArtifactCapturePolicy AssistantOutputArtifactCapturePolicy { get; set; } =
        AssistantAudioArtifactCapturePolicy.ContentStoreArtifact;

    public IAudioOutputSink? AssistantAudioOutputSink { get; set; }

    public bool EnableAssistantOutputPlayback { get; set; }

    public ITextToSpeechClient? AssistantOutputTextToSpeechClient { get; set; }

    public Func<IServiceProvider?, ITextToSpeechClient?>? AssistantOutputTextToSpeechClientFactory { get; set; }

    public string? AssistantOutputProviderKey { get; set; }

    public string? AssistantOutputModelId { get; set; }

    public string? AssistantOutputVoiceId { get; set; }

    public string? AssistantOutputLanguage { get; set; }

    public string? AssistantOutputFormat { get; set; }

    public string? AssistantOutputContentType { get; set; }

    public float? AssistantOutputSpeed { get; set; }
}

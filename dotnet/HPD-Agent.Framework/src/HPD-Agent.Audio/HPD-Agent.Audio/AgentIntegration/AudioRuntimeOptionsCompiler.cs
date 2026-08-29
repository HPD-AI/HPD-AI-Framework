using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;

namespace HPD.Agent.Audio.AgentIntegration;

public static class AudioRuntimeOptionsCompiler
{
    public static AudioRuntimeAttachmentOptions Compile(
        AudioRuntimeAttachmentOptions baseOptions,
        AudioConfig? agentAudio = null,
        AudioRunConfig? runAudio = null,
        TextToSpeechClientConfig? textToSpeech = null)
    {
        ArgumentNullException.ThrowIfNull(baseOptions);

        var options = Clone(baseOptions);
        Apply(options, agentAudio);
        Apply(options, runAudio);
        Apply(options, textToSpeech);
        return options;
    }

    private static AudioRuntimeAttachmentOptions Clone(AudioRuntimeAttachmentOptions source)
    {
        return new AudioRuntimeAttachmentOptions
        {
            Enabled = source.Enabled,
            RunAudioInteractionRuntime = source.RunAudioInteractionRuntime,
            RunAudioInteractionRuntimeForRealtimeTransport = source.RunAudioInteractionRuntimeForRealtimeTransport,
            AnnotateAudioInputMetadata = source.AnnotateAudioInputMetadata,
            ProjectCommittedTranscriptsIntoUserMessage = source.ProjectCommittedTranscriptsIntoUserMessage,
            SessionControlAuthority = source.SessionControlAuthority,
            PolicySet = source.PolicySet,
            ProviderRoute = source.ProviderRoute,
            ProviderRouteResolver = source.ProviderRouteResolver,
            ProviderCandidates = source.ProviderCandidates,
            InteractionSessionFactory = source.InteractionSessionFactory,
            InteractionSessionFactoryResolver = source.InteractionSessionFactoryResolver,
            ThreadProjectionSink = source.ThreadProjectionSink,
            AssistantOutputSynthesisMode = source.AssistantOutputSynthesisMode,
            AssistantOutputPacingOptions = source.AssistantOutputPacingOptions,
            AssistantOutputProgressiveRouteMode = source.AssistantOutputProgressiveRouteMode,
            AssistantOutputPushTextAggregationMode = source.AssistantOutputPushTextAggregationMode,
            AssistantOutputArtifactCapturePolicy = source.AssistantOutputArtifactCapturePolicy,
            AssistantAudioOutputSink = source.AssistantAudioOutputSink,
            EnableAssistantOutputPlayback = source.EnableAssistantOutputPlayback,
            AssistantOutputTextToSpeechClient = source.AssistantOutputTextToSpeechClient,
            AssistantOutputTextToSpeechClientFactory = source.AssistantOutputTextToSpeechClientFactory,
            AssistantOutputProviderKey = source.AssistantOutputProviderKey,
            AssistantOutputModelId = source.AssistantOutputModelId,
            AssistantOutputVoiceId = source.AssistantOutputVoiceId,
            AssistantOutputLanguage = source.AssistantOutputLanguage,
            AssistantOutputFormat = source.AssistantOutputFormat,
            AssistantOutputContentType = source.AssistantOutputContentType,
            AssistantOutputSpeed = source.AssistantOutputSpeed,
            PreparedOutputResolver = source.PreparedOutputResolver
        };
    }

    private static void Apply(AudioRuntimeAttachmentOptions options, AudioConfig? audio)
    {
        if (audio is null)
        {
            return;
        }

        options.Enabled = audio.Enabled;
        ApplyInputMode(options, audio.InputMode);

        if (audio.Policy is not null)
        {
            options.PolicySet = audio.Policy;
        }

        options.AssistantOutputSynthesisMode = audio.AssistantOutputMode;

        if (audio.Pacing is not null)
        {
            options.AssistantOutputPacingOptions = audio.Pacing;
        }

        options.AssistantOutputProgressiveRouteMode = audio.ProgressiveRouteMode;
        options.AssistantOutputPushTextAggregationMode = audio.PushTextAggregationMode;
        options.AssistantOutputArtifactCapturePolicy = audio.ArtifactCapturePolicy;
        options.EnableAssistantOutputPlayback = audio.EnablePlayback;
        ApplyOutputMode(options, audio.OutputMode);
    }

    private static void Apply(AudioRuntimeAttachmentOptions options, AudioRunConfig? audio)
    {
        if (audio is null)
        {
            return;
        }

        if (audio.Enabled.HasValue)
        {
            options.Enabled = audio.Enabled.Value;
        }

        if (audio.InputMode.HasValue)
        {
            ApplyInputMode(options, audio.InputMode.Value);
        }

        if (audio.AssistantOutputMode.HasValue)
        {
            options.AssistantOutputSynthesisMode = audio.AssistantOutputMode.Value;
        }

        if (audio.Pacing is not null)
        {
            options.AssistantOutputPacingOptions = audio.Pacing;
        }

        if (audio.ProgressiveRouteMode.HasValue)
        {
            options.AssistantOutputProgressiveRouteMode = audio.ProgressiveRouteMode.Value;
        }

        if (audio.PushTextAggregationMode.HasValue)
        {
            options.AssistantOutputPushTextAggregationMode = audio.PushTextAggregationMode.Value;
        }

        if (audio.ArtifactCapturePolicy.HasValue)
        {
            options.AssistantOutputArtifactCapturePolicy = audio.ArtifactCapturePolicy.Value;
        }

        if (!string.IsNullOrWhiteSpace(audio.ContentType))
        {
            options.AssistantOutputContentType = audio.ContentType;
        }

        if (audio.EnablePlayback.HasValue)
        {
            options.EnableAssistantOutputPlayback = audio.EnablePlayback.Value;
        }

        if (audio.OutputMode.HasValue)
        {
            ApplyOutputMode(options, audio.OutputMode.Value);
        }
    }

    private static void Apply(AudioRuntimeAttachmentOptions options, TextToSpeechClientConfig? textToSpeech)
    {
        if (textToSpeech is null)
            return;

        options.AssistantOutputVoiceId = textToSpeech.VoiceId ?? options.AssistantOutputVoiceId;
        options.AssistantOutputLanguage = textToSpeech.Language ?? options.AssistantOutputLanguage;
        options.AssistantOutputFormat = textToSpeech.AudioFormat ?? options.AssistantOutputFormat;
        options.AssistantOutputSpeed = textToSpeech.Speed ?? options.AssistantOutputSpeed;
    }

    private static void ApplyInputMode(AudioRuntimeAttachmentOptions options, AudioInputMode mode)
    {
        switch (mode)
        {
            case AudioInputMode.Auto:
            case AudioInputMode.ProviderRealtime:
                break;
            case AudioInputMode.None:
                options.RunAudioInteractionRuntime = false;
                break;
            case AudioInputMode.BatchSpeechToText:
                options.RunAudioInteractionRuntime = true;
                options.PolicySet = options.PolicySet with
                {
                    InputMedia = options.PolicySet.InputMedia with
                    {
                        HandlingMode = InputMediaHandlingMode.TranscribeOnly,
                        AllowBatchTranscription = true
                    }
                };
                break;
            case AudioInputMode.ReferenceOnly:
                options.RunAudioInteractionRuntime = true;
                options.PolicySet = options.PolicySet with
                {
                    InputMedia = options.PolicySet.InputMedia with
                    {
                        HandlingMode = InputMediaHandlingMode.ReferenceOnly,
                        AllowBatchTranscription = false
                    }
                };
                break;
            case AudioInputMode.Reject:
                options.RunAudioInteractionRuntime = true;
                options.PolicySet = options.PolicySet with
                {
                    InputMedia = options.PolicySet.InputMedia with
                    {
                        HandlingMode = InputMediaHandlingMode.Reject
                    }
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static void ApplyOutputMode(AudioRuntimeAttachmentOptions options, AudioOutputMode mode)
    {
        switch (mode)
        {
            case AudioOutputMode.Auto:
            case AudioOutputMode.ProviderRealtimeAudio:
                break;
            case AudioOutputMode.None:
            case AudioOutputMode.TextOnly:
                options.AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Disabled;
                break;
            case AudioOutputMode.TextToSpeech:
                if (options.AssistantOutputSynthesisMode == AssistantOutputSynthesisMode.Disabled)
                {
                    options.AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.FinalText;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}

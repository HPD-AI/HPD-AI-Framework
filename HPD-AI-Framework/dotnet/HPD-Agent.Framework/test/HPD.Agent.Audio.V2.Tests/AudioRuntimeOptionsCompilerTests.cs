using HPD.Agent;
using HPD.Agent.Audio.AgentIntegration;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class AudioRuntimeOptionsCompilerTests
{
    [Fact]
    public void Compile_AppliesAgentAudioDefaultsWithoutMutatingBaseOptions()
    {
        var baseOptions = new AudioRuntimeAttachmentOptions
        {
            Enabled = true,
            RunAudioInteractionRuntime = false,
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Disabled,
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.Auto,
            AssistantOutputPushTextAggregationMode = PushTextInputAggregationMode.ProviderDefault,
            AssistantOutputArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.WorkspaceArtifact,
            EnableAssistantOutputPlayback = false
        };
        var agentAudio = new AudioConfig
        {
            Enabled = false,
            InputMode = AudioInputMode.BatchSpeechToText,
            OutputMode = AudioOutputMode.TextToSpeech,
            AssistantOutputMode = AssistantOutputSynthesisMode.Progressive,
            ProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForcePushText,
            PushTextAggregationMode = PushTextInputAggregationMode.Sentence,
            ArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.MetadataOnly,
            EnablePlayback = true,
            Pacing = new TextToSpeechPacingOptions
            {
                Mode = TextToSpeechPacingMode.Phrase
            }
        };

        var compiled = AudioRuntimeOptionsCompiler.Compile(baseOptions, agentAudio);

        Assert.False(compiled.Enabled);
        Assert.True(compiled.RunAudioInteractionRuntime);
        Assert.Equal(InputMediaHandlingMode.TranscribeOnly, compiled.PolicySet.InputMedia.HandlingMode);
        Assert.True(compiled.PolicySet.InputMedia.AllowBatchTranscription);
        Assert.Equal(AssistantOutputSynthesisMode.Progressive, compiled.AssistantOutputSynthesisMode);
        Assert.Equal(ProgressiveTextToSpeechRouteMode.ForcePushText, compiled.AssistantOutputProgressiveRouteMode);
        Assert.Equal(PushTextInputAggregationMode.Sentence, compiled.AssistantOutputPushTextAggregationMode);
        Assert.Equal(AssistantAudioArtifactCapturePolicy.MetadataOnly, compiled.AssistantOutputArtifactCapturePolicy);
        Assert.True(compiled.EnableAssistantOutputPlayback);
        Assert.NotNull(compiled.AssistantOutputPacingOptions);
        Assert.Equal(TextToSpeechPacingMode.Phrase, compiled.AssistantOutputPacingOptions.Mode);

        Assert.True(baseOptions.Enabled);
        Assert.False(baseOptions.RunAudioInteractionRuntime);
        Assert.Equal(AssistantOutputSynthesisMode.Disabled, baseOptions.AssistantOutputSynthesisMode);
        Assert.Equal(InputMediaHandlingMode.RouteByProviderCapability, baseOptions.PolicySet.InputMedia.HandlingMode);
    }

    [Fact]
    public void Compile_RunAudioOverridesAgentAudioDefaults()
    {
        var agentAudio = new AudioConfig
        {
            Enabled = true,
            InputMode = AudioInputMode.BatchSpeechToText,
            OutputMode = AudioOutputMode.TextToSpeech,
            AssistantOutputMode = AssistantOutputSynthesisMode.Progressive,
            ArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.WorkspaceArtifact,
            EnablePlayback = false
        };
        var runAudio = new AudioRunConfig
        {
            Enabled = false,
            InputMode = AudioInputMode.ReferenceOnly,
            OutputMode = AudioOutputMode.TextOnly,
            AssistantOutputMode = AssistantOutputSynthesisMode.FinalText,
            ProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForceSegment,
            PushTextAggregationMode = PushTextInputAggregationMode.ManualFlush,
            ArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.DigestOnly,
            VoiceId = "voice-run",
            Language = "en",
            OutputFormat = "pcm16",
            ContentType = "audio/pcm",
            Speed = 1.1f,
            EnablePlayback = true,
            Pacing = new TextToSpeechPacingOptions
            {
                Mode = TextToSpeechPacingMode.TokenBatch
            }
        };

        var compiled = AudioRuntimeOptionsCompiler.Compile(
            new AudioRuntimeAttachmentOptions(),
            agentAudio,
            runAudio);

        Assert.False(compiled.Enabled);
        Assert.True(compiled.RunAudioInteractionRuntime);
        Assert.Equal(InputMediaHandlingMode.ReferenceOnly, compiled.PolicySet.InputMedia.HandlingMode);
        Assert.False(compiled.PolicySet.InputMedia.AllowBatchTranscription);
        Assert.Equal(AssistantOutputSynthesisMode.Disabled, compiled.AssistantOutputSynthesisMode);
        Assert.Equal(ProgressiveTextToSpeechRouteMode.ForceSegment, compiled.AssistantOutputProgressiveRouteMode);
        Assert.Equal(PushTextInputAggregationMode.ManualFlush, compiled.AssistantOutputPushTextAggregationMode);
        Assert.Equal(AssistantAudioArtifactCapturePolicy.DigestOnly, compiled.AssistantOutputArtifactCapturePolicy);
        Assert.Equal("voice-run", compiled.AssistantOutputVoiceId);
        Assert.Equal("en", compiled.AssistantOutputLanguage);
        Assert.Equal("pcm16", compiled.AssistantOutputFormat);
        Assert.Equal("audio/pcm", compiled.AssistantOutputContentType);
        Assert.Equal(1.1f, compiled.AssistantOutputSpeed);
        Assert.True(compiled.EnableAssistantOutputPlayback);
        Assert.NotNull(compiled.AssistantOutputPacingOptions);
        Assert.Equal(TextToSpeechPacingMode.TokenBatch, compiled.AssistantOutputPacingOptions.Mode);
    }

    [Theory]
    [InlineData(AudioInputMode.None, false, InputMediaHandlingMode.RouteByProviderCapability, true)]
    [InlineData(AudioInputMode.BatchSpeechToText, true, InputMediaHandlingMode.TranscribeOnly, true)]
    [InlineData(AudioInputMode.ReferenceOnly, true, InputMediaHandlingMode.ReferenceOnly, false)]
    [InlineData(AudioInputMode.Reject, true, InputMediaHandlingMode.Reject, true)]
    public void Compile_MapsInputModesToRuntimePolicy(
        AudioInputMode inputMode,
        bool runRuntime,
        InputMediaHandlingMode handlingMode,
        bool allowBatchTranscription)
    {
        var compiled = AudioRuntimeOptionsCompiler.Compile(
            new AudioRuntimeAttachmentOptions(),
            runAudio: new AudioRunConfig
            {
                InputMode = inputMode
            });

        Assert.Equal(runRuntime, compiled.RunAudioInteractionRuntime);
        Assert.Equal(handlingMode, compiled.PolicySet.InputMedia.HandlingMode);
        Assert.Equal(allowBatchTranscription, compiled.PolicySet.InputMedia.AllowBatchTranscription);
    }

    [Theory]
    [InlineData(AudioOutputMode.None, AssistantOutputSynthesisMode.Disabled)]
    [InlineData(AudioOutputMode.TextOnly, AssistantOutputSynthesisMode.Disabled)]
    [InlineData(AudioOutputMode.TextToSpeech, AssistantOutputSynthesisMode.FinalText)]
    public void Compile_MapsOutputModesToRuntimeSynthesis(
        AudioOutputMode outputMode,
        AssistantOutputSynthesisMode expectedMode)
    {
        var compiled = AudioRuntimeOptionsCompiler.Compile(
            new AudioRuntimeAttachmentOptions(),
            runAudio: new AudioRunConfig
            {
                OutputMode = outputMode
            });

        Assert.Equal(expectedMode, compiled.AssistantOutputSynthesisMode);
    }

    [Fact]
    public void Compile_TextToSpeechOutputModeKeepsExplicitAssistantMode()
    {
        var compiled = AudioRuntimeOptionsCompiler.Compile(
            new AudioRuntimeAttachmentOptions(),
            runAudio: new AudioRunConfig
            {
                OutputMode = AudioOutputMode.TextToSpeech,
                AssistantOutputMode = AssistantOutputSynthesisMode.ProgressiveWithFinalFallback
            });

        Assert.Equal(AssistantOutputSynthesisMode.ProgressiveWithFinalFallback, compiled.AssistantOutputSynthesisMode);
    }

    [Fact]
    public void Compile_TextOnlyOutputModeSuppressesExplicitAssistantMode()
    {
        var compiled = AudioRuntimeOptionsCompiler.Compile(
            new AudioRuntimeAttachmentOptions(),
            runAudio: new AudioRunConfig
            {
                OutputMode = AudioOutputMode.TextOnly,
                AssistantOutputMode = AssistantOutputSynthesisMode.ProgressiveWithFinalFallback
            });

        Assert.Equal(AssistantOutputSynthesisMode.Disabled, compiled.AssistantOutputSynthesisMode);
    }
}

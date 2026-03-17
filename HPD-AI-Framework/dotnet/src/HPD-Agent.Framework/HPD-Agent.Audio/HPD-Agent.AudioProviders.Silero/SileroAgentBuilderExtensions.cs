// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio;
using HPD.Agent.Audio.Vad;

namespace HPD.Agent.AudioProviders.Silero;

/// <summary>
/// AgentBuilder extension methods for adding Silero VAD.
/// </summary>
public static class SileroAgentBuilderExtensions
{
    /// <summary>
    /// Adds Silero VAD to the audio pipeline.
    /// Call after WithOpenAIAudio() or WithElevenLabsTts() to enable voice activity detection.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="activationThreshold">Speech probability threshold (0.0–1.0). Default: 0.5.</param>
    /// <param name="minSpeechDuration">Minimum speech duration in seconds before start event fires. Default: 50ms.</param>
    /// <param name="minSilenceDuration">Minimum silence duration in seconds before end event fires. Default: 550ms.</param>
    /// <param name="prefixPaddingDuration">Pre-speech audio to include at start of speech (seconds). Default: 500ms.</param>
    /// <returns>The agent builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithOpenAIAudio(apiKey: "sk-...")
    ///     .WithSileroVad()
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithSileroVad(
        this AgentBuilder builder,
        float activationThreshold = 0.5f,
        float minSpeechDuration = 0.05f,
        float minSilenceDuration = 0.55f,
        float prefixPaddingDuration = 0.5f)
    {
        var factory = new SileroVadProviderFactory();
        var config = new VadConfig
        {
            Provider = "silero-vad",
            ActivationThreshold = activationThreshold,
            MinSpeechDuration = minSpeechDuration,
            MinSilenceDuration = minSilenceDuration,
            PrefixPaddingDuration = prefixPaddingDuration
        };

        var detector = factory.CreateDetector(config);
        return builder.UseAudioPipeline(configure: m => m.Vad = detector);
    }
}

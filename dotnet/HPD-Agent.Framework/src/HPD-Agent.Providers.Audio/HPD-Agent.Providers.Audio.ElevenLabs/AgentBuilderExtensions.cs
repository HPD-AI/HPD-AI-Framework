// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

/// <summary>
/// Extension methods for configuring ElevenLabs audio provider families on <see cref="AgentBuilder"/>.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures ElevenLabs as the speech-to-text provider.
    /// </summary>
    public static AgentBuilder WithElevenLabsSpeechToText(
        this AgentBuilder builder,
        string? model = null,
        string? apiKey = null,
        Action<ElevenLabsSttConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providerConfig = new ElevenLabsSttConfig
        {
            DefaultModelId = model
        };
        configure?.Invoke(providerConfig);

        var clientConfig = new ProviderClientConfig
        {
            ProviderKey = ElevenLabsAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.SpeechToText, clientConfig);
        clientConfig.SetProviderConfig(providerConfig, ProviderClientFamily.SpeechToText);

        return builder;
    }

    /// <summary>
    /// Configures ElevenLabs as the text-to-speech provider.
    /// </summary>
    public static AgentBuilder WithElevenLabsTextToSpeech(
        this AgentBuilder builder,
        string? model = null,
        string? apiKey = null,
        string? voice = null,
        string? outputFormat = null,
        Action<ElevenLabsTtsConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providerConfig = new ElevenLabsTtsConfig
        {
            DefaultModelId = model,
            DefaultVoiceId = voice,
            OutputFormat = outputFormat
        };
        configure?.Invoke(providerConfig);

        var clientConfig = new ProviderClientConfig
        {
            ProviderKey = ElevenLabsAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.TextToSpeech, clientConfig);
        clientConfig.SetProviderConfig(providerConfig, ProviderClientFamily.TextToSpeech);

        return builder;
    }
}

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
        string? language = null,
        Action<ElevenLabsSttConfig>? configureClient = null,
        Action<ElevenLabsSttOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providerConfig = new ElevenLabsSttConfig();
        configureClient?.Invoke(providerConfig);
        var providerOptions = new ElevenLabsSttOptions();
        configureOptions?.Invoke(providerOptions);

        var clientConfig = new SpeechToTextClientConfig
        {
            ProviderKey = ElevenLabsAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model,
            SpeechLanguage = language,
            ProviderConfig = providerConfig,
            ProviderOptions = providerOptions
        };

        builder.Config.SetClientConfig(ProviderClientFamily.SpeechToText, clientConfig);
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
        float? speed = null,
        Action<ElevenLabsTtsConfig>? configureClient = null,
        Action<ElevenLabsTtsOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providerConfig = new ElevenLabsTtsConfig();
        configureClient?.Invoke(providerConfig);
        var providerOptions = new ElevenLabsTtsOptions();
        configureOptions?.Invoke(providerOptions);

        var clientConfig = new TextToSpeechClientConfig
        {
            ProviderKey = ElevenLabsAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model,
            VoiceId = voice,
            AudioFormat = outputFormat,
            Speed = speed,
            ProviderConfig = providerConfig,
            ProviderOptions = providerOptions
        };

        builder.Config.SetClientConfig(ProviderClientFamily.TextToSpeech, clientConfig);
        return builder;
    }
}

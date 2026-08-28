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
        ProviderAuthentication? authentication = null,
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
            Provider = new ProviderReference
            {
                Key = ElevenLabsAudioProvider.Key,
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "elevenlabs:ApiKey" }
            },
            ModelName = model,
            SpeechLanguage = language,
            ProviderConfig = providerConfig,
            ProviderOptions = providerOptions
        };

        builder.Config.SetClientConfig(ProviderClientFamily.SpeechToText, clientConfig);
        return builder;
    }

    /// <summary>Configures ElevenLabs speech-to-text with a literal runtime-only API key.</summary>
    public static AgentBuilder WithElevenLabsSpeechToText(this AgentBuilder builder, string? model, ReadOnlySpan<char> apiKey, string? language = null, Action<ElevenLabsSttConfig>? configureClient = null, Action<ElevenLabsSttOptions>? configureOptions = null) =>
        builder.WithElevenLabsSpeechToText(model, builder.RegisterExplicitApiKey(apiKey), language, configureClient, configureOptions);

    /// <summary>
    /// Configures ElevenLabs as the text-to-speech provider.
    /// </summary>
    public static AgentBuilder WithElevenLabsTextToSpeech(
        this AgentBuilder builder,
        string? model = null,
        ProviderAuthentication? authentication = null,
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
            Provider = new ProviderReference
            {
                Key = ElevenLabsAudioProvider.Key,
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "elevenlabs:ApiKey" }
            },
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

    /// <summary>Configures ElevenLabs text-to-speech with a literal runtime-only API key.</summary>
    public static AgentBuilder WithElevenLabsTextToSpeech(this AgentBuilder builder, string? model, ReadOnlySpan<char> apiKey, string? voice = null, string? outputFormat = null, float? speed = null, Action<ElevenLabsTtsConfig>? configureClient = null, Action<ElevenLabsTtsOptions>? configureOptions = null) =>
        builder.WithElevenLabsTextToSpeech(model, builder.RegisterExplicitApiKey(apiKey), voice, outputFormat, speed, configureClient, configureOptions);
}

// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Audio.OpenAI;

/// <summary>
/// Extension methods for configuring OpenAI audio provider families on <see cref="AgentBuilder"/>.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures OpenAI as the speech-to-text provider.
    /// </summary>
    public static AgentBuilder WithOpenAISpeechToText(
        this AgentBuilder builder,
        string? model = null,
        string? apiKey = null,
        Action<OpenAISttOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var clientConfig = new SpeechToTextClientConfig
        {
            ProviderKey = OpenAIAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.SpeechToText, clientConfig);
        if (configure is not null)
        {
            var providerOptions = new OpenAISttOptions();
            configure(providerOptions);
            clientConfig.ProviderOptions = providerOptions;
        }

        return builder;
    }

    /// <summary>
    /// Configures OpenAI as the text-to-speech provider.
    /// </summary>
    public static AgentBuilder WithOpenAITextToSpeech(
        this AgentBuilder builder,
        string? model = null,
        string? apiKey = null,
        string? voice = null,
        string? outputFormat = null,
        float? speed = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var clientConfig = new TextToSpeechClientConfig
        {
            ProviderKey = OpenAIAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model,
            VoiceId = voice,
            AudioFormat = outputFormat,
            Speed = speed
        };

        builder.Config.SetClientConfig(ProviderClientFamily.TextToSpeech, clientConfig);
        return builder;
    }

    /// <summary>
    /// Configures OpenAI as the realtime audio provider.
    /// </summary>
    public static AgentBuilder WithOpenAIRealtime(
        this AgentBuilder builder,
        string? model = null,
        string? apiKey = null,
        Action<OpenAIRealtimeConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providerConfig = new OpenAIRealtimeConfig();
        configure?.Invoke(providerConfig);

        var clientConfig = new RealtimeClientConfig
        {
            ProviderKey = OpenAIAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.Realtime, clientConfig);
        clientConfig.ProviderConfig = providerConfig;

        return builder;
    }
}

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
        Action<OpenAISttConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providerConfig = new OpenAISttConfig
        {
            DefaultModelId = model
        };
        configure?.Invoke(providerConfig);

        var clientConfig = new ProviderClientConfig
        {
            ProviderKey = OpenAIAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.SpeechToText, clientConfig);
        clientConfig.SetProviderConfig(providerConfig, ProviderClientFamily.SpeechToText);

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
        Action<OpenAITtsConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providerConfig = new OpenAITtsConfig
        {
            DefaultModelId = model,
            DefaultVoiceId = voice,
            OutputFormat = outputFormat
        };
        configure?.Invoke(providerConfig);

        var clientConfig = new ProviderClientConfig
        {
            ProviderKey = OpenAIAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.TextToSpeech, clientConfig);
        clientConfig.SetProviderConfig(providerConfig, ProviderClientFamily.TextToSpeech);

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

        var providerConfig = new OpenAIRealtimeConfig
        {
            DefaultModelId = model
        };
        configure?.Invoke(providerConfig);

        var clientConfig = new ProviderClientConfig
        {
            ProviderKey = OpenAIAudioProvider.Key,
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.Realtime, clientConfig);
        clientConfig.SetProviderConfig(providerConfig, ProviderClientFamily.Realtime);

        return builder;
    }
}

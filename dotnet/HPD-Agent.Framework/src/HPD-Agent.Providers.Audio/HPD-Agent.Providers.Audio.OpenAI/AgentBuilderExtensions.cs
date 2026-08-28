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
        ProviderAuthentication? authentication = null,
        Action<OpenAISttOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var clientConfig = new SpeechToTextClientConfig
        {
            Provider = new ProviderReference
            {
                Key = OpenAIAudioProvider.Key,
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "openai:ApiKey" }
            },
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
    /// <summary>Configures OpenAI speech-to-text with a literal runtime-only API key.</summary>
    public static AgentBuilder WithOpenAISpeechToText(this AgentBuilder builder, string? model, ReadOnlySpan<char> apiKey, Action<OpenAISttOptions>? configure = null) => builder.WithOpenAISpeechToText(model, builder.RegisterExplicitApiKey(apiKey), configure);

    /// <summary>
    /// Configures OpenAI as the text-to-speech provider.
    /// </summary>
    public static AgentBuilder WithOpenAITextToSpeech(
        this AgentBuilder builder,
        string? model = null,
        ProviderAuthentication? authentication = null,
        string? voice = null,
        string? outputFormat = null,
        float? speed = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var clientConfig = new TextToSpeechClientConfig
        {
            Provider = new ProviderReference
            {
                Key = OpenAIAudioProvider.Key,
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "openai:ApiKey" }
            },
            ModelName = model,
            VoiceId = voice,
            AudioFormat = outputFormat,
            Speed = speed
        };

        builder.Config.SetClientConfig(ProviderClientFamily.TextToSpeech, clientConfig);
        return builder;
    }
    /// <summary>Configures OpenAI text-to-speech with a literal runtime-only API key.</summary>
    public static AgentBuilder WithOpenAITextToSpeech(this AgentBuilder builder, string? model, ReadOnlySpan<char> apiKey, string? voice = null, string? outputFormat = null, float? speed = null) => builder.WithOpenAITextToSpeech(model, builder.RegisterExplicitApiKey(apiKey), voice, outputFormat, speed);

    /// <summary>
    /// Configures OpenAI as the realtime audio provider.
    /// </summary>
    public static AgentBuilder WithOpenAIRealtime(
        this AgentBuilder builder,
        string? model = null,
        ProviderAuthentication? authentication = null,
        Action<OpenAIRealtimeConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providerConfig = new OpenAIRealtimeConfig();
        configure?.Invoke(providerConfig);

        var clientConfig = new RealtimeClientConfig
        {
            Provider = new ProviderReference
            {
                Key = OpenAIAudioProvider.Key,
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "openai:ApiKey" }
            },
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.Realtime, clientConfig);
        clientConfig.ProviderConfig = providerConfig;

        return builder;
    }
    /// <summary>Configures OpenAI realtime with a literal runtime-only API key.</summary>
    public static AgentBuilder WithOpenAIRealtime(this AgentBuilder builder, string? model, ReadOnlySpan<char> apiKey, Action<OpenAIRealtimeConfig>? configure = null) => builder.WithOpenAIRealtime(model, builder.RegisterExplicitApiKey(apiKey), configure);
}

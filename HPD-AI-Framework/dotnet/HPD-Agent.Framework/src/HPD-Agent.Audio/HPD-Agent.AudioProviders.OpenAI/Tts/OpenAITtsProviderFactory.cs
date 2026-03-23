// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Audio;
using HPD.Agent.Audio.OpenAI;
using HPD.Agent.Audio.Tts;

namespace HPD.Agent.AudioProviders.OpenAI.Tts;

public class OpenAITtsProviderFactory : ITtsProviderFactory
{
    public ITextToSpeechClient CreateClient(TtsConfig config, IServiceProvider? services = null)
    {
        // Deserialize provider-specific config
        var providerConfig = string.IsNullOrEmpty(config.ProviderOptionsJson)
            ? new OpenAITtsConfig()
            : JsonSerializer.Deserialize<OpenAITtsConfig>(config.ProviderOptionsJson, OpenAITtsJsonContext.Default.OpenAITtsConfig)!;

        // Resolve API key
        var apiKey = providerConfig.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key is required. Set it via ProviderOptionsJson or OPENAI_API_KEY environment variable.");

        // Create client with MERGED config (service-agnostic + provider-specific)
        return new OpenAITextToSpeechClient(
            apiKey: apiKey,
            model: config.ModelId ?? "tts-1",
            voice: config.Voice ?? "alloy"
        );

        // Note: OpenAITextToSpeechClient doesn't currently support all TtsConfig properties
        // (baseUrl, timeout, speed, outputFormat, sampleRate, pitch, language)
        // These would need to be added to the constructor in a future update
    }

    public TtsProviderMetadata GetMetadata() => new()
    {
        ProviderKey = "openai-audio",
        DisplayName = "OpenAI TTS",
        SupportsStreaming = true,
        SupportedVoices = ["alloy", "echo", "fable", "onyx", "nova", "shimmer", "ash", "coral", "sage"],
        SupportedFormats = ["mp3", "opus", "aac", "flac", "wav", "pcm"],
        SupportedLanguages = null, // All languages supported
        DocumentationUrl = "https://platform.openai.com/docs/guides/text-to-speech"
    };

    public ValidationResult Validate(TtsConfig config)
    {
        var errors = new List<string>();

        // Validate service-agnostic settings
        if (config.Speed is < 0.25f or > 4.0f)
            errors.Add("Speed must be between 0.25 and 4.0");

        // Validate provider-specific settings
        if (!string.IsNullOrEmpty(config.ProviderOptionsJson))
        {
            try
            {
                var providerConfig = JsonSerializer.Deserialize<OpenAITtsConfig>(
                    config.ProviderOptionsJson,
                    OpenAITtsJsonContext.Default.OpenAITtsConfig);

                var apiKey = providerConfig?.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrWhiteSpace(apiKey))
                    errors.Add("OpenAI API key is required");
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid ProviderOptionsJson: {ex.Message}");
            }
        }
        else
        {
            // No JSON provided, check environment variable
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                errors.Add("OpenAI API key is required (provide via ProviderOptionsJson or OPENAI_API_KEY environment variable)");
        }

        return errors.Count > 0
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }
}

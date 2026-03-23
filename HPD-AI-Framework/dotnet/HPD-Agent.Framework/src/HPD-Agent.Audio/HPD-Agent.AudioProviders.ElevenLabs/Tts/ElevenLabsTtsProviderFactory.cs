// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Tts;

namespace HPD.Agent.AudioProviders.ElevenLabs.Tts;

public class ElevenLabsTtsProviderFactory : ITtsProviderFactory
{
    public ITextToSpeechClient CreateClient(TtsConfig config, IServiceProvider? services = null)
    {
        // Deserialize provider-specific config
        var providerConfig = string.IsNullOrEmpty(config.ProviderOptionsJson)
            ? new ElevenLabsTtsConfig()
            : JsonSerializer.Deserialize<ElevenLabsTtsConfig>(config.ProviderOptionsJson, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig)!;

        // Resolve API key
        var apiKey = providerConfig.ApiKey ?? Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ElevenLabs API key is required. Set it via ProviderOptionsJson or ELEVENLABS_API_KEY environment variable.");

        // Apply defaults to provider config if not specified
        providerConfig.ApiKey ??= apiKey;
        providerConfig.Stability ??= 0.5f;
        providerConfig.SimilarityBoost ??= 0.75f;
        providerConfig.Style ??= 0.0f;
        providerConfig.UseSpeakerBoost ??= true;
        providerConfig.EnableWordTimestamps ??= false;

        // Create client using role-based configuration
        var httpClient = services?.GetService(typeof(HttpClient)) as HttpClient;
        return new HPD.Agent.Audio.ElevenLabs.ElevenLabsTextToSpeechClient(
            ttsConfig: config,
            providerConfig: providerConfig,
            httpClient: httpClient
        );
    }

    public TtsProviderMetadata GetMetadata() => new()
    {
        ProviderKey = "elevenlabs",
        DisplayName = "ElevenLabs TTS",
        SupportsStreaming = true,
        SupportedVoices = null, // User-created voices, not enumerable
        SupportedFormats = ["mp3_44100_128", "mp3_44100_192", "pcm_16000", "pcm_22050", "pcm_24000", "pcm_44100", "ulaw_8000"],
        SupportedLanguages = ["en", "es", "fr", "de", "it", "pt", "pl", "tr", "ru", "nl", "cs", "ar", "zh", "ja", "hi", "ko"],
        DocumentationUrl = "https://elevenlabs.io/docs"
    };

    public ValidationResult Validate(TtsConfig config)
    {
        var errors = new List<string>();

        // Validate service-agnostic settings
        if (config.Speed is < 0.5f or > 2.0f)
            errors.Add("Speed must be between 0.5 and 2.0 for ElevenLabs");

        // Validate provider-specific settings
        if (!string.IsNullOrEmpty(config.ProviderOptionsJson))
        {
            try
            {
                var providerConfig = JsonSerializer.Deserialize<ElevenLabsTtsConfig>(
                    config.ProviderOptionsJson,
                    ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig);

                var apiKey = providerConfig?.ApiKey ?? Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
                if (string.IsNullOrWhiteSpace(apiKey))
                    errors.Add("ElevenLabs API key is required");

                if (providerConfig?.Stability is < 0.0f or > 1.0f)
                    errors.Add("Stability must be between 0.0 and 1.0");

                if (providerConfig?.SimilarityBoost is < 0.0f or > 1.0f)
                    errors.Add("SimilarityBoost must be between 0.0 and 1.0");

                if (providerConfig?.Style is < 0.0f or > 1.0f)
                    errors.Add("Style must be between 0.0 and 1.0");

                if (providerConfig?.Seed is < 0)
                    errors.Add("Seed must be non-negative");

                if (providerConfig?.PreviousRequestIds != null && providerConfig.PreviousRequestIds.Length > 3)
                    errors.Add("Cannot have more than 3 previous request IDs");

                if (providerConfig?.NextRequestIds != null && providerConfig.NextRequestIds.Length > 3)
                    errors.Add("Cannot have more than 3 next request IDs");

                if (providerConfig?.ApplyTextNormalization != null &&
                    providerConfig.ApplyTextNormalization != "on" &&
                    providerConfig.ApplyTextNormalization != "off" &&
                    providerConfig.ApplyTextNormalization != "auto")
                    errors.Add("ApplyTextNormalization must be 'on', 'off', or 'auto'");
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid ProviderOptionsJson: {ex.Message}");
            }
        }
        else
        {
            // No JSON provided, check environment variable
            var apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                errors.Add("ElevenLabs API key is required (provide via ProviderOptionsJson or ELEVENLABS_API_KEY environment variable)");
        }

        return errors.Count > 0
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }
}

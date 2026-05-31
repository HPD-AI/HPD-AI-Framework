// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Audio.ElevenLabs;
using HPD.Agent.Audio.Tts;
using HPD.Agent.AudioProviders.ElevenLabs.Tts;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.AudioProviders.ElevenLabs;

internal sealed class ElevenLabsProvider : ITextToSpeechClientProvider
{
    public string ProviderKey => "elevenlabs";
    public string DisplayName => "ElevenLabs";

    public ITextToSpeechClient CreateTextToSpeechClient(
        ClientProviderConfig config,
        IServiceProvider? services = null)
    {
        var providerConfig = string.IsNullOrEmpty(config.ProviderOptionsJson)
            ? new ElevenLabsTtsConfig()
            : JsonSerializer.Deserialize(
                config.ProviderOptionsJson,
                ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig)
              ?? new ElevenLabsTtsConfig();

        var apiKey = config.ApiKey ?? providerConfig.ApiKey ?? Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ElevenLabs API key is required. Set it via ClientProviderConfig.ApiKey, ProviderOptionsJson, or ELEVENLABS_API_KEY.");

        providerConfig.ApiKey ??= apiKey;
        providerConfig.Stability ??= 0.5f;
        providerConfig.SimilarityBoost ??= 0.75f;
        providerConfig.Style ??= 0.0f;
        providerConfig.UseSpeakerBoost ??= true;
        providerConfig.EnableWordTimestamps ??= false;

        var ttsConfig = new TtsConfig
        {
            Provider = ProviderKey,
            ModelId = config.ModelName,
            Voice = GetStringProperty(config, "voice"),
            OutputFormat = GetStringProperty(config, "outputFormat"),
            ProviderOptionsJson = config.ProviderOptionsJson,
            AdditionalProperties = config.AdditionalProperties
        };

        var httpClient = services?.GetService(typeof(HttpClient)) as HttpClient;
        return new ElevenLabsTextToSpeechClient(ttsConfig, providerConfig, httpClient);
    }

    public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        DocumentationUri = new Uri("https://elevenlabs.io/docs"),
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.TextToSpeech] = new()
            {
                Family = ProviderClientFamily.TextToSpeech,
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsAudio"] = true
                }
            }
        }
    };

    public ProviderValidationResult ValidateConfiguration(
        ClientProviderConfig config,
        ProviderClientFamily family)
    {
        var errors = new List<string>();

        if (family != ProviderClientFamily.TextToSpeech)
            errors.Add($"ElevenLabs does not support provider family '{family}'.");

        if (string.IsNullOrWhiteSpace(config.ApiKey) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY")))
            errors.Add("ElevenLabs API key is required.");

        if (!string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
        {
            try
            {
                _ = JsonSerializer.Deserialize(
                    config.ProviderOptionsJson,
                    ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig);
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid ProviderOptionsJson: {ex.Message}");
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static string? GetStringProperty(ClientProviderConfig config, string key)
    {
        if (config.AdditionalProperties?.TryGetValue(key, out var value) != true)
            return null;

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value.ToString()
        };
    }
}

// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Audio.OpenAI;
using HPD.Agent.AudioProviders.OpenAI.Stt;
using HPD.Agent.AudioProviders.OpenAI.Tts;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.AudioProviders.OpenAI;

internal sealed class OpenAIAudioProvider : ITextToSpeechClientProvider, ISpeechToTextClientProvider
{
    public string ProviderKey => "openai";
    public string DisplayName => "OpenAI Audio";

    public ITextToSpeechClient CreateTextToSpeechClient(
        ClientProviderConfig config,
        IServiceProvider? services = null)
    {
        var providerConfig = string.IsNullOrEmpty(config.ProviderOptionsJson)
            ? new OpenAITtsConfig()
            : JsonSerializer.Deserialize(config.ProviderOptionsJson, OpenAITtsJsonContext.Default.OpenAITtsConfig)
              ?? new OpenAITtsConfig();

        var apiKey = config.ApiKey ?? providerConfig.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key is required. Set it via ClientProviderConfig.ApiKey, ProviderOptionsJson, or OPENAI_API_KEY.");

        return new OpenAITextToSpeechClient(
            apiKey: apiKey,
            model: config.ModelName.NullIfWhiteSpace() ?? "tts-1",
            voice: config.GetStringProperty("voice") ?? "alloy");
    }

    public ISpeechToTextClient CreateSpeechToTextClient(
        ClientProviderConfig config,
        IServiceProvider? services = null)
    {
        var providerConfig = string.IsNullOrEmpty(config.ProviderOptionsJson)
            ? new OpenAISttConfig()
            : JsonSerializer.Deserialize(config.ProviderOptionsJson, OpenAISttJsonContext.Default.OpenAISttConfig)
              ?? new OpenAISttConfig();

        var apiKey = config.ApiKey ?? providerConfig.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key is required. Set it via ClientProviderConfig.ApiKey, ProviderOptionsJson, or OPENAI_API_KEY.");

        return new OpenAISpeechToTextClient(
            apiKey: apiKey,
            model: config.ModelName.NullIfWhiteSpace() ?? "whisper-1",
            baseUrl: providerConfig.BaseUrl);
    }

    public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        DocumentationUri = new Uri("https://platform.openai.com/docs/guides/audio"),
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.TextToSpeech] = new()
            {
                Family = ProviderClientFamily.TextToSpeech,
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsAudio"] = true
                }
            },
            [ProviderClientFamily.SpeechToText] = new()
            {
                Family = ProviderClientFamily.SpeechToText,
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

        if (family is not (ProviderClientFamily.TextToSpeech or ProviderClientFamily.SpeechToText))
            errors.Add($"OpenAI audio does not support provider family '{family}'.");

        if (string.IsNullOrWhiteSpace(config.ApiKey) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
            errors.Add("OpenAI API key is required.");

        if (!string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
        {
            try
            {
                if (family == ProviderClientFamily.TextToSpeech)
                    _ = JsonSerializer.Deserialize(config.ProviderOptionsJson, OpenAITtsJsonContext.Default.OpenAITtsConfig);
                else
                    _ = JsonSerializer.Deserialize(config.ProviderOptionsJson, OpenAISttJsonContext.Default.OpenAISttConfig);
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
}

internal static class ClientProviderConfigAudioExtensions
{
    public static string? GetStringProperty(this ClientProviderConfig config, string key)
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

    public static string? NullIfWhiteSpace(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

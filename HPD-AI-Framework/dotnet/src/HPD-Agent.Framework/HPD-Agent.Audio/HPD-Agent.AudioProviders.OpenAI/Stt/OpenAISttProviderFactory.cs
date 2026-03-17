// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Stt;
using Microsoft.Extensions.AI;

namespace HPD.Agent.AudioProviders.OpenAI.Stt;

public class OpenAISttProviderFactory : ISttProviderFactory
{
    public ISpeechToTextClient CreateClient(SttConfig config, IServiceProvider? services = null)
    {
        // Deserialize provider-specific config
        var providerConfig = string.IsNullOrEmpty(config.ProviderOptionsJson)
            ? new OpenAISttConfig()
            : JsonSerializer.Deserialize<OpenAISttConfig>(config.ProviderOptionsJson, OpenAISttJsonContext.Default.OpenAISttConfig)!;

        // Resolve API key
        var apiKey = providerConfig.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key is required. Set it via ProviderOptionsJson or OPENAI_API_KEY environment variable.");

        // TODO: Implement OpenAISpeechToTextClient when ready
        // For now, throw NotImplementedException
        throw new NotImplementedException("OpenAI STT client is not yet implemented. Use Deepgram or another STT provider.");

        // Future implementation:
        // return new OpenAISpeechToTextClient(
        //     apiKey: apiKey,
        //     model: config.ModelId ?? "whisper-1",
        //     language: config.Language,
        //     temperature: config.Temperature,
        //     responseFormat: config.ResponseFormat
        // );
    }

    public SttProviderMetadata GetMetadata() => new()
    {
        ProviderKey = "openai-audio",
        DisplayName = "OpenAI Whisper STT",
        SupportsStreaming = false, // Whisper API doesn't support streaming
        SupportedLanguages = null, // All languages supported
        SupportedFormats = ["mp3", "mp4", "mpeg", "mpga", "m4a", "wav", "webm"],
        DocumentationUrl = "https://platform.openai.com/docs/guides/speech-to-text"
    };

    public ValidationResult Validate(SttConfig config)
    {
        var errors = new List<string>();

        // Validate service-agnostic settings
        if (config.Temperature is < 0.0f or > 1.0f)
            errors.Add("Temperature must be between 0.0 and 1.0");

        // Validate provider-specific settings
        if (!string.IsNullOrEmpty(config.ProviderOptionsJson))
        {
            try
            {
                var providerConfig = JsonSerializer.Deserialize<OpenAISttConfig>(
                    config.ProviderOptionsJson,
                    OpenAISttJsonContext.Default.OpenAISttConfig);

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

// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

public sealed class ElevenLabsAudioProvider : ITextToSpeechClientProvider, ISpeechToTextClientProvider
{
    public const string Key = "elevenlabs";
    public const string DefaultBaseUrl = "https://api.elevenlabs.io/v1";
    public const string DefaultWebSocketBaseUrl = "wss://api.elevenlabs.io/v1";
    public const string DefaultSpeechToTextModel = "scribe_v1";
    public const string DefaultRealtimeSpeechToTextModel = "scribe_v2_realtime";
    public const string DefaultTextToSpeechModel = "eleven_turbo_v2_5";
    public const string DefaultVoiceId = "21m00Tcm4TlvDq8ikWAM";
    public const string DefaultOutputFormat = "mp3_44100_128";

    public string ProviderKey => Key;

    public string DisplayName => "ElevenLabs Audio";

    public ISpeechToTextClient CreateSpeechToTextClient(
        ClientProviderConfig config,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerConfig = ReadSttProviderConfig(config);
        providerConfig.DefaultModelId = FirstNonWhiteSpace(
            config.ModelName,
            providerConfig.DefaultModelId,
            DefaultSpeechToTextModel);

        var apiKey = ResolveApiKey(config, providerConfig.ApiKey, services, "speech-to-text");
        var httpClient = services?.GetService(typeof(HttpClient)) as HttpClient;
        return new ElevenLabsSpeechToTextClient(apiKey, providerConfig, httpClient);
    }

    public ITextToSpeechClient CreateTextToSpeechClient(
        ClientProviderConfig config,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerConfig = ReadProviderConfig(config);
        providerConfig.DefaultModelId = FirstNonWhiteSpace(
            config.ModelName,
            providerConfig.DefaultModelId,
            DefaultTextToSpeechModel);

        var apiKey = ResolveApiKey(config, providerConfig.ApiKey, services, "text-to-speech");
        var httpClient = services?.GetService(typeof(HttpClient)) as HttpClient;
        return new ElevenLabsTextToSpeechClient(apiKey, providerConfig, httpClient);
    }

    public IProviderErrorHandler CreateErrorHandler() => new ElevenLabsErrorHandler();

    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        DocumentationUri = new Uri("https://elevenlabs.io/docs/api-reference/text-to-speech/convert"),
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.SpeechToText] = new()
            {
                Family = ProviderClientFamily.SpeechToText,
                DefaultModelId = DefaultSpeechToTextModel,
                SupportedModels =
                [
                    DefaultSpeechToTextModel,
                    "scribe_v2",
                    DefaultRealtimeSpeechToTextModel
                ],
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsAudio"] = true,
                    ["SupportsStreamingSpeechToText"] = true,
                    ["SupportsRealtimeSpeechToText"] = true
                }
            },
            [ProviderClientFamily.TextToSpeech] = new()
            {
                Family = ProviderClientFamily.TextToSpeech,
                DefaultModelId = DefaultTextToSpeechModel,
                SupportedModels =
                [
                    DefaultTextToSpeechModel,
                    "eleven_multilingual_v2",
                    "eleven_flash_v2_5"
                ],
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsAudio"] = true,
                    ["SupportsCompletedTextSynthesis"] = true,
                    ["SupportsPushTextAudioStreaming"] = false,
                    ["PreferredStreamingFormats"] = Array.Empty<string>(),
                    ["DefaultVoiceId"] = DefaultVoiceId
                }
            }
        }
    };

    public ProviderValidationResult ValidateConfiguration(
        ClientProviderConfig config,
        ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family is not (ProviderClientFamily.SpeechToText or ProviderClientFamily.TextToSpeech))
        {
            errors.Add($"ElevenLabs audio does not support provider family '{family}'.");
        }

        ElevenLabsTtsConfig? providerConfig = null;
        ElevenLabsSttConfig? sttProviderConfig = null;
        if (!string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
        {
            try
            {
                if (family == ProviderClientFamily.SpeechToText)
                {
                    sttProviderConfig = ReadSttProviderConfig(config);
                }
                else
                {
                    providerConfig = ReadProviderConfig(config);
                }
            }
            catch (JsonException ex)
            {
                var label = family == ProviderClientFamily.SpeechToText ? "STT" : "TTS";
                errors.Add($"Invalid ElevenLabs {label} ProviderOptionsJson: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey) &&
            string.IsNullOrWhiteSpace(providerConfig?.ApiKey) &&
            string.IsNullOrWhiteSpace(sttProviderConfig?.ApiKey) &&
            string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY")))
        {
            var label = family == ProviderClientFamily.SpeechToText ? "speech-to-text" : "text-to-speech";
            errors.Add($"ElevenLabs API key is required for {label}.");
        }

        if (providerConfig is not null)
        {
            AddRangeError(errors, providerConfig.Stability, "stability");
            AddRangeError(errors, providerConfig.SimilarityBoost, "similarityBoost");
            AddRangeError(errors, providerConfig.Style, "style");
        }

        return errors.Count == 0
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(errors.ToArray());
    }

    private static string ResolveApiKey(
        ClientProviderConfig config,
        string? providerApiKey,
        IServiceProvider? services,
        string familyLabel)
    {
        var configured = FirstNonWhiteSpace(config.ApiKey, providerApiKey);
        if (configured is not null)
        {
            return configured;
        }

        var secrets = services?.GetService(typeof(ISecretResolver)) as ISecretResolver;
        if (secrets is not null)
        {
            var resolved = secrets
                .ResolveAsync("elevenlabs:ApiKey", CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (!string.IsNullOrWhiteSpace(resolved?.Value))
            {
                return resolved.Value.Value;
            }
        }

        var environmentValue = System.Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        throw new InvalidOperationException(
            $"ElevenLabs API key is required for {familyLabel}. " +
            "Set ClientProviderConfig.ApiKey, provider options apiKey, provide an ISecretResolver with key " +
            "'elevenlabs:ApiKey', or set ELEVENLABS_API_KEY.");
    }

    private static ElevenLabsTtsConfig ReadProviderConfig(ClientProviderConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
        {
            return new ElevenLabsTtsConfig();
        }

        return JsonSerializer.Deserialize(
            config.ProviderOptionsJson,
            ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig)
            ?? new ElevenLabsTtsConfig();
    }

    private static ElevenLabsSttConfig ReadSttProviderConfig(ClientProviderConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
        {
            return new ElevenLabsSttConfig();
        }

        return JsonSerializer.Deserialize(
            config.ProviderOptionsJson,
            ElevenLabsTtsJsonContext.Default.ElevenLabsSttConfig)
            ?? new ElevenLabsSttConfig();
    }

    private static void AddRangeError(List<string> errors, double? value, string name)
    {
        if (value is < 0 or > 1)
        {
            errors.Add($"ElevenLabs {name} must be between 0 and 1.");
        }
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

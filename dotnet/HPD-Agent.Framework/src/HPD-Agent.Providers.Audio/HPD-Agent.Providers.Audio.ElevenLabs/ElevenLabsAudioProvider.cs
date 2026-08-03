// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

[HpdProvider("elevenlabs", "ElevenLabs Audio")]
[HpdProviderFamily(ProviderClientFamily.TextToSpeech)]
[HpdProviderFamily(ProviderClientFamily.SpeechToText)]
[HpdProviderPayload(ProviderClientFamily.TextToSpeech, ProviderPayloadKind.Configuration, typeof(ElevenLabsTtsConfig), typeof(ElevenLabsTtsJsonContext))]
[HpdProviderPayload(ProviderClientFamily.SpeechToText, ProviderPayloadKind.Configuration, typeof(ElevenLabsSttConfig), typeof(ElevenLabsTtsJsonContext))]
[HpdProviderPayload(ProviderClientFamily.TextToSpeech, ProviderPayloadKind.OperationOptions, typeof(ElevenLabsTtsOptions), typeof(ElevenLabsTtsJsonContext))]
[HpdProviderPayload(ProviderClientFamily.SpeechToText, ProviderPayloadKind.OperationOptions, typeof(ElevenLabsSttOptions), typeof(ElevenLabsTtsJsonContext))]
[HpdProviderSecretAlias("elevenlabs:ApiKey", "ELEVENLABS_API_KEY")]
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
        ProviderClientConfig config,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerConfig = ReadSttProviderConfig(config);
        var providerOptions = (config as SpeechToTextClientConfig)?.ProviderOptions as ElevenLabsSttOptions;
        var familyConfig = config as SpeechToTextClientConfig;
        var runtimeSettings = new ElevenLabsSttRuntimeSettings
        {
            BaseUrl = config.Endpoint,
            WebSocketBaseUrl = providerConfig.WebSocketBaseUrl,
            DefaultModelId = FirstNonWhiteSpace(config.ModelName, DefaultSpeechToTextModel),
            RealtimeModelId = providerOptions?.RealtimeModelId,
            LanguageCode = familyConfig?.SpeechLanguage,
            Diarize = providerOptions?.Diarize,
            TagAudioEvents = providerOptions?.TagAudioEvents,
            TimestampsGranularity = providerOptions?.TimestampsGranularity,
            AudioFormat = providerOptions?.AudioFormat,
            CommitStrategy = providerOptions?.CommitStrategy,
            IncludeTimestamps = providerOptions?.IncludeTimestamps,
            IncludeLanguageDetection = providerOptions?.IncludeLanguageDetection,
            Keyterms = providerOptions?.Keyterms,
            NoVerbatim = providerOptions?.NoVerbatim,
            VadSilenceThresholdSeconds = providerOptions?.VadSilenceThresholdSeconds,
            VadThreshold = providerOptions?.VadThreshold,
            MinSpeechDurationMilliseconds = providerOptions?.MinSpeechDurationMilliseconds,
            MinSilenceDurationMilliseconds = providerOptions?.MinSilenceDurationMilliseconds,
            EnableLogging = providerOptions?.EnableLogging,
            StreamingChunkSizeBytes = providerOptions?.StreamingChunkSizeBytes
        };
        var apiKey = ResolveApiKey(config, services, "speech-to-text");
        var httpClient = services?.GetService(typeof(HttpClient)) as HttpClient;
        return new ElevenLabsSpeechToTextClient(apiKey, runtimeSettings, httpClient);
    }

    public ITextToSpeechClient CreateTextToSpeechClient(
        ProviderClientConfig config,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerConfig = ReadProviderConfig(config);
        var providerOptions = (config as TextToSpeechClientConfig)?.ProviderOptions as ElevenLabsTtsOptions;
        var familyConfig = config as TextToSpeechClientConfig;
        var runtimeSettings = new ElevenLabsTtsRuntimeSettings
        {
            BaseUrl = config.Endpoint,
            WebSocketBaseUrl = providerConfig.WebSocketBaseUrl,
            DefaultModelId = FirstNonWhiteSpace(config.ModelName, DefaultTextToSpeechModel),
            DefaultVoiceId = FirstNonWhiteSpace(familyConfig?.VoiceId, DefaultVoiceId),
            OutputFormat = FirstNonWhiteSpace(familyConfig?.AudioFormat, DefaultOutputFormat),
            Speed = familyConfig?.Speed,
            Stability = providerOptions?.Stability,
            SimilarityBoost = providerOptions?.SimilarityBoost,
            Style = providerOptions?.Style,
            UseSpeakerBoost = providerOptions?.UseSpeakerBoost,
            ApplyTextNormalization = providerOptions?.ApplyTextNormalization,
            EnablePushTextStreaming = providerConfig.EnablePushTextStreaming,
            AutoMode = providerOptions?.AutoMode,
            SyncAlignment = providerOptions?.SyncAlignment,
            InactivityTimeout = providerOptions?.InactivityTimeout
        };
        var apiKey = ResolveApiKey(config, services, "text-to-speech");
        var httpClient = services?.GetService(typeof(HttpClient)) as HttpClient;
        return new ElevenLabsTextToSpeechClient(apiKey, runtimeSettings, httpClient);
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
        ProviderClientConfig config,
        ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family is not (ProviderClientFamily.SpeechToText or ProviderClientFamily.TextToSpeech))
        {
            errors.Add($"ElevenLabs audio does not support provider family '{family}'.");
        }

        if (config.ProviderConfig is not null)
        {
            if (family == ProviderClientFamily.SpeechToText)
            {
                _ = ReadSttProviderConfig(config);
            }
            else
            {
                _ = ReadProviderConfig(config);
            }
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey) &&
            string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY")))
        {
            var label = family == ProviderClientFamily.SpeechToText ? "speech-to-text" : "text-to-speech";
            errors.Add($"ElevenLabs API key is required for {label}.");
        }

        if ((config as TextToSpeechClientConfig)?.ProviderOptions is ElevenLabsTtsOptions options)
        {
            AddRangeError(errors, options.Stability, "stability");
            AddRangeError(errors, options.SimilarityBoost, "similarityBoost");
            AddRangeError(errors, options.Style, "style");
        }

        return errors.Count == 0
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(errors.ToArray());
    }

    private static string ResolveApiKey(
        ProviderClientConfig config,
        IServiceProvider? services,
        string familyLabel)
    {
        var configured = FirstNonWhiteSpace(config.ApiKey);
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
            "Set ProviderClientConfig.ApiKey, provide an ISecretResolver with key " +
            "'elevenlabs:ApiKey', or set ELEVENLABS_API_KEY.");
    }

    private static ElevenLabsTtsConfig ReadProviderConfig(ProviderClientConfig config)
    {
        return config.ProviderConfig as ElevenLabsTtsConfig ?? new ElevenLabsTtsConfig();
    }

    private static ElevenLabsSttConfig ReadSttProviderConfig(ProviderClientConfig config)
    {
        return config.ProviderConfig as ElevenLabsSttConfig ?? new ElevenLabsSttConfig();
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

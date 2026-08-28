// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

[HpdProvider("elevenlabs", "ElevenLabs Audio")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "elevenlabs:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.TextToSpeech)]
[HpdProviderFamily(ProviderClientFamily.SpeechToText)]
[HpdProviderPayload(ProviderClientFamily.TextToSpeech, ProviderPayloadKind.Configuration, typeof(ElevenLabsTtsConfig), typeof(ElevenLabsTtsJsonContext))]
[HpdProviderPayload(ProviderClientFamily.SpeechToText, ProviderPayloadKind.Configuration, typeof(ElevenLabsSttConfig), typeof(ElevenLabsTtsJsonContext))]
[HpdProviderPayload(ProviderClientFamily.TextToSpeech, ProviderPayloadKind.OperationOptions, typeof(ElevenLabsTtsOptions), typeof(ElevenLabsTtsJsonContext))]
[HpdProviderPayload(ProviderClientFamily.SpeechToText, ProviderPayloadKind.OperationOptions, typeof(ElevenLabsSttOptions), typeof(ElevenLabsTtsJsonContext))]
[HpdProviderSecretAlias("elevenlabs:ApiKey", "ELEVENLABS_API_KEY")]
public sealed class ElevenLabsAudioProvider : IProvider,
    IProviderClientFactory<ITextToSpeechClient>,
    IProviderClientFactory<ISpeechToTextClient>
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

    ProviderClientCredentialBinding IProviderClientFactory<ISpeechToTextClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<ITextToSpeechClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);

    ValueTask<ProviderClientConstruction<ISpeechToTextClient>> IProviderClientFactory<ISpeechToTextClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var config = context.EffectiveConfig;
        var providerConfig = ReadSttProviderConfig(config);
        var providerOptions = ReadSttOptions(config);
        var runtimeSettings = new ElevenLabsSttRuntimeSettings
        {
            BaseUrl = config.Endpoint?.AbsoluteUri,
            WebSocketBaseUrl = providerConfig.WebSocketBaseUrl,
            DefaultModelId = FirstNonWhiteSpace(config.ModelName, DefaultSpeechToTextModel),
            RealtimeModelId = providerOptions?.RealtimeModelId,
            LanguageCode = config.FamilyDefaults.Language,
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
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var httpClient = context.Services.HttpClientFactory.CreateClient("hpd.elevenlabs.speech-to-text");
        ISpeechToTextClient client = new ElevenLabsSpeechToTextClient(apiKey, runtimeSettings, httpClient);
        return Construct(client, httpClient);
    }

    ValueTask<ProviderClientConstruction<ITextToSpeechClient>> IProviderClientFactory<ITextToSpeechClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var config = context.EffectiveConfig;
        var providerConfig = ReadProviderConfig(config);
        var providerOptions = ReadTtsOptions(config);
        var runtimeSettings = new ElevenLabsTtsRuntimeSettings
        {
            BaseUrl = config.Endpoint?.AbsoluteUri,
            WebSocketBaseUrl = providerConfig.WebSocketBaseUrl,
            DefaultModelId = FirstNonWhiteSpace(config.ModelName, DefaultTextToSpeechModel),
            DefaultVoiceId = FirstNonWhiteSpace(config.FamilyDefaults.VoiceId, DefaultVoiceId),
            OutputFormat = FirstNonWhiteSpace(config.FamilyDefaults.MediaType, DefaultOutputFormat),
            Speed = config.FamilyDefaults.Speed,
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
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var httpClient = context.Services.HttpClientFactory.CreateClient("hpd.elevenlabs.text-to-speech");
        ITextToSpeechClient client = new ElevenLabsTextToSpeechClient(apiKey, runtimeSettings, httpClient);
        return Construct(client, httpClient);
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
        EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (config.Family is not (ProviderClientFamily.SpeechToText or ProviderClientFamily.TextToSpeech))
        {
            errors.Add($"ElevenLabs audio does not support provider family '{config.Family}'.");
        }

        if (!config.ProviderConfiguration.CanonicalPayload.IsEmpty)
        {
            if (config.Family == ProviderClientFamily.SpeechToText)
            {
                _ = ReadSttProviderConfig(config);
            }
            else
            {
                _ = ReadProviderConfig(config);
            }
        }

        if (config.Family == ProviderClientFamily.TextToSpeech && ReadTtsOptions(config) is { } options)
        {
            AddRangeError(errors, options.Stability, "stability");
            AddRangeError(errors, options.SimilarityBoost, "similarityBoost");
            AddRangeError(errors, options.Style, "style");
        }

        return errors.Count == 0
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(errors.ToArray());
    }

    private static ElevenLabsTtsConfig ReadProviderConfig(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty ? new ElevenLabsTtsConfig() :
            JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(), ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig) ?? new ElevenLabsTtsConfig();

    private static ElevenLabsSttConfig ReadSttProviderConfig(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty ? new ElevenLabsSttConfig() :
            JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(), ElevenLabsTtsJsonContext.Default.ElevenLabsSttConfig) ?? new ElevenLabsSttConfig();

    private static ElevenLabsTtsOptions? ReadTtsOptions(EffectiveProviderClientConfig config) =>
        config.FamilyOperation.CanonicalPayload.IsEmpty ? null :
            JsonSerializer.Deserialize(config.FamilyOperation.CanonicalPayload.AsSpan(), ElevenLabsTtsJsonContext.Default.ElevenLabsTtsOptions);

    private static ElevenLabsSttOptions? ReadSttOptions(EffectiveProviderClientConfig config) =>
        config.FamilyOperation.CanonicalPayload.IsEmpty ? null :
            JsonSerializer.Deserialize(config.FamilyOperation.CanonicalPayload.AsSpan(), ElevenLabsTtsJsonContext.Default.ElevenLabsSttOptions);

    private static ProviderClientCredentialBinding ResolveBinding(ProviderClientBindingDescriptor descriptor)
    { ArgumentNullException.ThrowIfNull(descriptor); return ProviderClientCredentialBinding.ConstructionTime; }

    private static void ValidateContext(ProviderClientConstructionContext context, CancellationToken cancellationToken)
    { ArgumentNullException.ThrowIfNull(context); cancellationToken.ThrowIfCancellationRequested(); }

    private static ValueTask<ProviderClientConstruction<TClient>> Construct<TClient>(TClient client, HttpClient httpClient) where TClient : class =>
        ValueTask.FromResult(new ProviderClientConstruction<TClient>
        { Client = client, Owner = ProviderClientConstructionUtilities.Own(httpClient, client) });

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

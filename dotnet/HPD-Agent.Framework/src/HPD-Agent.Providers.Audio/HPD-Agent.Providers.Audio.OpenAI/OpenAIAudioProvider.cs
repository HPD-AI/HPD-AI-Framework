// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using System.Text.Json;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Realtime;

namespace HPD.Agent.Providers.Audio.OpenAI;

#pragma warning disable OPENAI002

[HpdProvider("openai", "OpenAI")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "openai:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.SpeechToText)]
[HpdProviderFamily(ProviderClientFamily.TextToSpeech)]
[HpdProviderFamily(ProviderClientFamily.Realtime)]
[HpdProviderPayload(ProviderClientFamily.SpeechToText, ProviderPayloadKind.OperationOptions, typeof(OpenAISttOptions), typeof(OpenAISttJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Realtime, ProviderPayloadKind.Configuration, typeof(OpenAIRealtimeConfig), typeof(OpenAIRealtimeJsonContext))]
[HpdProviderSecretAlias("openai:ApiKey", "OPENAI_API_KEY")]
[HpdProviderSecretAlias("openai:Endpoint", "OPENAI_ENDPOINT")]
public sealed class OpenAIAudioProvider : IProvider,
    IProviderClientFactory<ISpeechToTextClient>,
    IProviderClientFactory<ITextToSpeechClient>,
    IProviderClientFactory<IRealtimeClient>
{
    public const string Key = "openai";
    public const string DefaultSpeechToTextModel = "whisper-1";
    public const string DefaultRealtimeSpeechToTextModel = "gpt-live-transcribe";
    public const string DefaultTextToSpeechModel = "tts-1";
    public const string DefaultTextToSpeechVoice = "nova";
    public const string DefaultTextToSpeechOutputFormat = "mp3";
    public const string DefaultRealtimeModel = "gpt-realtime";

    public string ProviderKey => Key;

    public string DisplayName => "OpenAI Audio";

    ProviderClientCredentialBinding IProviderClientFactory<ISpeechToTextClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<ITextToSpeechClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IRealtimeClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);

    ValueTask<ProviderClientConstruction<ISpeechToTextClient>> IProviderClientFactory<ISpeechToTextClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var config = context.EffectiveConfig;
        var providerOptions = config.FamilyOperation.CanonicalPayload.IsEmpty ? new OpenAISttOptions() :
            JsonSerializer.Deserialize(config.FamilyOperation.CanonicalPayload.AsSpan(), OpenAISttJsonContext.Default.OpenAISttOptions) ?? new OpenAISttOptions();
        var modelName = FirstNonWhiteSpace(config.ModelName, DefaultSpeechToTextModel)!;
        var openAIClient = CreateOpenAIClient(context);
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var endpoint = config.Endpoint ?? new Uri("https://api.openai.com/v1");

        ISpeechToTextClient client = new OpenAIConfiguringSpeechToTextClient(
            openAIClient.GetAudioClient(modelName).AsISpeechToTextClient(),
            providerOptions, apiKey, endpoint,
            string.IsNullOrWhiteSpace(config.ModelName) ? DefaultRealtimeSpeechToTextModel : modelName,
            config.FamilyDefaults.Language,
            config.CustomHeaders);
        return Construct(client);
    }

    ValueTask<ProviderClientConstruction<ITextToSpeechClient>> IProviderClientFactory<ITextToSpeechClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var config = context.EffectiveConfig;
        var modelName = FirstNonWhiteSpace(config.ModelName, DefaultTextToSpeechModel)!;
        var voiceId = FirstNonWhiteSpace(config.FamilyDefaults.VoiceId, DefaultTextToSpeechVoice)!;
        var outputFormat = FirstNonWhiteSpace(config.FamilyDefaults.MediaType, DefaultTextToSpeechOutputFormat)!;
        var openAIClient = CreateOpenAIClient(context);

        ITextToSpeechClient client = new OpenAITextToSpeechClient(
            openAIClient.GetAudioClient(modelName),
            modelName,
            voiceId,
            outputFormat);
        return Construct(client);
    }

    ValueTask<ProviderClientConstruction<IRealtimeClient>> IProviderClientFactory<IRealtimeClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var config = context.EffectiveConfig;
        var providerConfig = ReadRealtimeProviderConfig(config);
        var modelName = FirstNonWhiteSpace(config.ModelName, DefaultRealtimeModel)!;
        var realtimeClient = CreateOpenAIRealtimeClient(context, providerConfig);

        IRealtimeClient client = new OpenAIRealtimeClient(realtimeClient, modelName);
        return Construct(client);
    }

    public IProviderErrorHandler CreateErrorHandler() => new OpenAIAudioErrorHandler();

    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        DocumentationUri = new Uri("https://platform.openai.com/docs/guides/audio"),
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.SpeechToText] = new()
            {
                Family = ProviderClientFamily.SpeechToText,
                DefaultModelId = DefaultSpeechToTextModel,
                SupportedModels =
                [
                    DefaultSpeechToTextModel,
                    "gpt-4o-transcribe",
                    "gpt-4o-mini-transcribe",
                    DefaultRealtimeSpeechToTextModel,
                    "gpt-transcribe"
                ],
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsAudio"] = true,
                    ["SupportsRetainedStreamingTranscription"] = true,
                    ["RetainedStreamingSampleRateHz"] = 24000
                }
            },
            [ProviderClientFamily.TextToSpeech] = new()
            {
                Family = ProviderClientFamily.TextToSpeech,
                DefaultModelId = DefaultTextToSpeechModel,
                SupportedModels =
                [
                    DefaultTextToSpeechModel,
                    "tts-1-hd",
                    "gpt-4o-mini-tts"
                ],
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsAudio"] = true,
                    ["SupportsCompletedTextSynthesis"] = true,
                    ["SupportsCompletedTextAudioStreaming"] = false,
                    ["SupportsPushTextAudioStreaming"] = false,
                    ["PreferredStreamingFormats"] = Array.Empty<string>(),
                    ["DefaultVoiceId"] = DefaultTextToSpeechVoice
                }
            },
            [ProviderClientFamily.Realtime] = new()
            {
                Family = ProviderClientFamily.Realtime,
                Lifetime = ProviderFamilyLifetime.StatefulPerAudioSession,
                DefaultModelId = DefaultRealtimeModel,
                SupportedModels =
                [
                    DefaultRealtimeModel,
                    "gpt-realtime-mini",
                    "gpt-4o-realtime-preview",
                    "gpt-4o-mini-realtime-preview"
                ],
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsRealtime"] = true,
                    ["SupportsAudioInput"] = true,
                    ["SupportsAudioOutput"] = true,
                    ["SupportsTextInput"] = true,
                    ["SupportsTextOutput"] = true,
                    ["SupportsToolCalls"] = true,
                    ["SupportsSessionUpdate"] = true
                }
            }
        }
    };

    public ProviderValidationResult ValidateConfiguration(
        EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (config.Family is not (ProviderClientFamily.SpeechToText or ProviderClientFamily.TextToSpeech or ProviderClientFamily.Realtime))
        {
            errors.Add($"OpenAI audio does not support provider family '{config.Family}'.");
        }

        if (!config.ProviderConfiguration.CanonicalPayload.IsEmpty)
        {
            if (config.Family == ProviderClientFamily.Realtime)
            {
                _ = ReadRealtimeProviderConfig(config);
            }
            else
            {
                errors.Add($"OpenAI {config.Family} does not define provider client configuration; use portable family fields and ProviderOptions.");
            }
        }

        return errors.Count == 0
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(errors.ToArray());
    }

    private static OpenAIClient CreateOpenAIClient(ProviderClientConstructionContext context)
    {
        var config = context.EffectiveConfig;
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var hasCustomEndpoint = config.Endpoint is not null;
        var hasCustomHeaders = config.CustomHeaders?.Count > 0;
        var options = new OpenAIClientOptions();

        if (hasCustomEndpoint)
        {
            options.Endpoint = config.Endpoint!;
        }

        if (hasCustomHeaders)
        {
            var httpClient = new HttpClient();
            foreach (var header in config.CustomHeaders!)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
            options.Transport = new HttpClientPipelineTransport(httpClient);
        }

        return new OpenAIClient(new ApiKeyCredential(apiKey), options);
    }

    private static RealtimeClient CreateOpenAIRealtimeClient(
        ProviderClientConstructionContext context,
        OpenAIRealtimeConfig providerConfig)
    {
        var config = context.EffectiveConfig;
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var options = new RealtimeClientOptions
        {
            OrganizationId = FirstNonWhiteSpace(providerConfig.OrganizationId),
            ProjectId = FirstNonWhiteSpace(providerConfig.ProjectId)
        };

        if (config.Endpoint is not null)
        {
            options.Endpoint = config.Endpoint;
        }

        return new RealtimeClient(new ApiKeyCredential(apiKey), options);
    }

    private static OpenAIRealtimeConfig ReadRealtimeProviderConfig(EffectiveProviderClientConfig config)
    {
        return config.ProviderConfiguration.CanonicalPayload.IsEmpty ? new OpenAIRealtimeConfig() :
            JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(), OpenAIRealtimeJsonContext.Default.OpenAIRealtimeConfig)
                ?? new OpenAIRealtimeConfig();
    }

    private static ProviderClientCredentialBinding ResolveBinding(ProviderClientBindingDescriptor descriptor)
    { ArgumentNullException.ThrowIfNull(descriptor); return ProviderClientCredentialBinding.ConstructionTime; }

    private static void ValidateContext(ProviderClientConstructionContext context, CancellationToken cancellationToken)
    { ArgumentNullException.ThrowIfNull(context); cancellationToken.ThrowIfCancellationRequested(); }

    private static ValueTask<ProviderClientConstruction<TClient>> Construct<TClient>(TClient client) where TClient : class =>
        ValueTask.FromResult(new ProviderClientConstruction<TClient>
        { Client = client, Owner = ProviderClientConstructionUtilities.Own(client) });

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

#pragma warning restore OPENAI002

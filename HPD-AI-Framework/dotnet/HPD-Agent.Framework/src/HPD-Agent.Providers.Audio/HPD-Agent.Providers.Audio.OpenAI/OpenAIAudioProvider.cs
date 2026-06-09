// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using System.Text.Json;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Realtime;

namespace HPD.Agent.Providers.Audio.OpenAI;

#pragma warning disable OPENAI002

public sealed class OpenAIAudioProvider : ISpeechToTextClientProvider, ITextToSpeechClientProvider, IRealtimeClientProvider
{
    public const string Key = "openai";
    public const string DefaultSpeechToTextModel = "whisper-1";
    public const string DefaultTextToSpeechModel = "tts-1";
    public const string DefaultTextToSpeechVoice = "nova";
    public const string DefaultTextToSpeechOutputFormat = "mp3";
    public const string DefaultRealtimeModel = "gpt-realtime";

    public string ProviderKey => Key;

    public string DisplayName => "OpenAI Audio";

    public ISpeechToTextClient CreateSpeechToTextClient(
        ClientProviderConfig config,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerConfig = ReadProviderConfig(config);
        var modelName = FirstNonWhiteSpace(
            config.ModelName,
            providerConfig.DefaultModelId,
            DefaultSpeechToTextModel)!;
        var openAIClient = CreateOpenAIClient(config, providerConfig, services);

        return new OpenAIConfiguringSpeechToTextClient(
            openAIClient.GetAudioClient(modelName).AsISpeechToTextClient(),
            providerConfig);
    }

    public ITextToSpeechClient CreateTextToSpeechClient(
        ClientProviderConfig config,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerConfig = ReadTtsProviderConfig(config);
        var modelName = FirstNonWhiteSpace(
            config.ModelName,
            providerConfig.DefaultModelId,
            DefaultTextToSpeechModel)!;
        var voiceId = FirstNonWhiteSpace(
            providerConfig.DefaultVoiceId,
            DefaultTextToSpeechVoice)!;
        var outputFormat = FirstNonWhiteSpace(
            providerConfig.OutputFormat,
            DefaultTextToSpeechOutputFormat)!;
        var openAIClient = CreateOpenAIClient(config, providerConfig, services);

        return new OpenAITextToSpeechClient(
            openAIClient.GetAudioClient(modelName),
            providerConfig,
            modelName,
            voiceId,
            outputFormat);
    }

    public IRealtimeClient CreateRealtimeClient(
        ClientProviderConfig config,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerConfig = ReadRealtimeProviderConfig(config);
        var modelName = FirstNonWhiteSpace(
            config.ModelName,
            providerConfig.DefaultModelId,
            DefaultRealtimeModel)!;
        var realtimeClient = CreateOpenAIRealtimeClient(config, providerConfig, services);

        return new OpenAIRealtimeClient(realtimeClient, modelName);
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
                    "gpt-4o-mini-transcribe"
                ],
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsAudio"] = true
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
        ClientProviderConfig config,
        ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family is not (ProviderClientFamily.SpeechToText or ProviderClientFamily.TextToSpeech or ProviderClientFamily.Realtime))
        {
            errors.Add($"OpenAI audio does not support provider family '{family}'.");
        }

        OpenAISttConfig? providerConfig = null;
        OpenAITtsConfig? ttsProviderConfig = null;
        OpenAIRealtimeConfig? realtimeProviderConfig = null;
        if (!string.IsNullOrWhiteSpace(config.GetProviderOptionsRawJson()))
        {
            try
            {
                if (family == ProviderClientFamily.TextToSpeech)
                {
                    ttsProviderConfig = ReadTtsProviderConfig(config);
                }
                else if (family == ProviderClientFamily.Realtime)
                {
                    realtimeProviderConfig = ReadRealtimeProviderConfig(config);
                }
                else
                {
                    providerConfig = ReadProviderConfig(config);
                }
            }
            catch (JsonException ex)
            {
                var label = family switch
                {
                    ProviderClientFamily.TextToSpeech => "TTS",
                    ProviderClientFamily.Realtime => "realtime",
                    _ => "STT"
                };
                errors.Add($"Invalid OpenAI {label} ProviderOptions: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey) &&
            string.IsNullOrWhiteSpace(providerConfig?.ApiKey) &&
            string.IsNullOrWhiteSpace(ttsProviderConfig?.ApiKey) &&
            string.IsNullOrWhiteSpace(realtimeProviderConfig?.ApiKey))
        {
            errors.Add("OpenAI API key is required for audio.");
        }

        return errors.Count == 0
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(errors.ToArray());
    }

    private static OpenAIClient CreateOpenAIClient(
        ClientProviderConfig config,
        OpenAISttConfig providerConfig,
        IServiceProvider? services)
    {
        var secrets = services?.GetService(typeof(ISecretResolver)) as ISecretResolver;
        var apiKey = ResolveApiKey(config, providerConfig, secrets);
        var endpoint = ResolveEndpoint(config, providerConfig, secrets);
        var hasCustomEndpoint = !string.IsNullOrWhiteSpace(endpoint);
        var hasCustomHeaders = config.CustomHeaders?.Count > 0;
        var options = new OpenAIClientOptions();

        if (hasCustomEndpoint)
        {
            options.Endpoint = new Uri(endpoint!, UriKind.Absolute);
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

    private static OpenAIClient CreateOpenAIClient(
        ClientProviderConfig config,
        OpenAITtsConfig providerConfig,
        IServiceProvider? services)
    {
        var secrets = services?.GetService(typeof(ISecretResolver)) as ISecretResolver;
        var apiKey = ResolveApiKey(config, providerConfig, secrets);
        var endpoint = ResolveEndpoint(config, providerConfig, secrets);
        var hasCustomEndpoint = !string.IsNullOrWhiteSpace(endpoint);
        var hasCustomHeaders = config.CustomHeaders?.Count > 0;
        var options = new OpenAIClientOptions();

        if (hasCustomEndpoint)
        {
            options.Endpoint = new Uri(endpoint!, UriKind.Absolute);
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
        ClientProviderConfig config,
        OpenAIRealtimeConfig providerConfig,
        IServiceProvider? services)
    {
        var secrets = services?.GetService(typeof(ISecretResolver)) as ISecretResolver;
        var apiKey = ResolveApiKey(config, providerConfig, secrets);
        var endpoint = ResolveEndpoint(config, providerConfig, secrets);
        var options = new RealtimeClientOptions
        {
            OrganizationId = FirstNonWhiteSpace(providerConfig.OrganizationId),
            ProjectId = FirstNonWhiteSpace(providerConfig.ProjectId)
        };

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            options.Endpoint = new Uri(endpoint!, UriKind.Absolute);
        }

        return new RealtimeClient(new ApiKeyCredential(apiKey), options);
    }

    private static string ResolveApiKey(
        ClientProviderConfig config,
        OpenAISttConfig providerConfig,
        ISecretResolver? secrets)
    {
        var configured = FirstNonWhiteSpace(config.ApiKey, providerConfig.ApiKey);
        if (configured is not null)
        {
            return configured;
        }

        if (secrets is not null)
        {
            return secrets
                .RequireAsync("openai:ApiKey", "OpenAI API Key", cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        throw new InvalidOperationException(
            "OpenAI API key is required for speech-to-text. " +
            "Set ClientProviderConfig.ApiKey, provider options apiKey, or provide an ISecretResolver with key 'openai:ApiKey'.");
    }

    private static string ResolveApiKey(
        ClientProviderConfig config,
        OpenAITtsConfig providerConfig,
        ISecretResolver? secrets)
    {
        var configured = FirstNonWhiteSpace(config.ApiKey, providerConfig.ApiKey);
        if (configured is not null)
        {
            return configured;
        }

        if (secrets is not null)
        {
            return secrets
                .RequireAsync("openai:ApiKey", "OpenAI API Key", cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        throw new InvalidOperationException(
            "OpenAI API key is required for text-to-speech. " +
            "Set ClientProviderConfig.ApiKey, provider options apiKey, or provide an ISecretResolver with key 'openai:ApiKey'.");
    }

    private static string ResolveApiKey(
        ClientProviderConfig config,
        OpenAIRealtimeConfig providerConfig,
        ISecretResolver? secrets)
    {
        var configured = FirstNonWhiteSpace(config.ApiKey, providerConfig.ApiKey);
        if (configured is not null)
        {
            return configured;
        }

        if (secrets is not null)
        {
            return secrets
                .RequireAsync("openai:ApiKey", "OpenAI API Key", cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        throw new InvalidOperationException(
            "OpenAI API key is required for realtime. " +
            "Set ClientProviderConfig.ApiKey, provider options apiKey, or provide an ISecretResolver with key 'openai:ApiKey'.");
    }

    private static string? ResolveEndpoint(
        ClientProviderConfig config,
        OpenAISttConfig providerConfig,
        ISecretResolver? secrets)
    {
        var configured = FirstNonWhiteSpace(config.Endpoint, providerConfig.BaseUrl);
        if (configured is not null || secrets is null)
        {
            return configured;
        }

        return secrets
            .ResolveOrDefaultAsync("openai:Endpoint", cancellationToken: CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static string? ResolveEndpoint(
        ClientProviderConfig config,
        OpenAITtsConfig providerConfig,
        ISecretResolver? secrets)
    {
        var configured = FirstNonWhiteSpace(config.Endpoint, providerConfig.BaseUrl);
        if (configured is not null || secrets is null)
        {
            return configured;
        }

        return secrets
            .ResolveOrDefaultAsync("openai:Endpoint", cancellationToken: CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static string? ResolveEndpoint(
        ClientProviderConfig config,
        OpenAIRealtimeConfig providerConfig,
        ISecretResolver? secrets)
    {
        var configured = FirstNonWhiteSpace(config.Endpoint, providerConfig.BaseUrl);
        if (configured is not null || secrets is null)
        {
            return configured;
        }

        return secrets
            .ResolveOrDefaultAsync("openai:Endpoint", cancellationToken: CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static OpenAISttConfig ReadProviderConfig(ClientProviderConfig config)
    {
        var providerOptionsJson = config.GetProviderOptionsRawJson();
        if (string.IsNullOrWhiteSpace(providerOptionsJson))
        {
            return new OpenAISttConfig();
        }

        return JsonSerializer.Deserialize(
            providerOptionsJson,
            OpenAISttJsonContext.Default.OpenAISttConfig)
            ?? new OpenAISttConfig();
    }

    private static OpenAITtsConfig ReadTtsProviderConfig(ClientProviderConfig config)
    {
        var providerOptionsJson = config.GetProviderOptionsRawJson();
        if (string.IsNullOrWhiteSpace(providerOptionsJson))
        {
            return new OpenAITtsConfig();
        }

        return JsonSerializer.Deserialize(
            providerOptionsJson,
            OpenAITtsJsonContext.Default.OpenAITtsConfig)
            ?? new OpenAITtsConfig();
    }

    private static OpenAIRealtimeConfig ReadRealtimeProviderConfig(ClientProviderConfig config)
    {
        var providerOptionsJson = config.GetProviderOptionsRawJson();
        if (string.IsNullOrWhiteSpace(providerOptionsJson))
        {
            return new OpenAIRealtimeConfig();
        }

        return JsonSerializer.Deserialize(
            providerOptionsJson,
            OpenAIRealtimeJsonContext.Default.OpenAIRealtimeConfig)
            ?? new OpenAIRealtimeConfig();
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

#pragma warning restore OPENAI002

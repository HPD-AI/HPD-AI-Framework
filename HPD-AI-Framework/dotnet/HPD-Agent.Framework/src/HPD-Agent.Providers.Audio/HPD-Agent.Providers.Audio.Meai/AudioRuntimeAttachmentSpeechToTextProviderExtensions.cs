using HPD.Agent;
using HPD.Agent.Audio.AgentIntegration;
using HPD.Agent.Audio.Media;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Audio.Meai;

public static class AudioRuntimeAttachmentSpeechToTextProviderExtensions
{
    public static AudioRuntimeAttachmentOptions UseSpeechToTextProvider(
        this AudioRuntimeAttachmentOptions audio,
        IProviderRegistry providerRegistry,
        ClientProviderConfig providerConfig,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(providerConfig);

        return audio.UseSpeechToTextProvider(
            providerRegistry,
            new InputMediaSpeechToTextProviderOptions
            {
                ProviderConfig = providerConfig,
                ProviderKey = providerConfig.ProviderKey,
                ModelId = string.IsNullOrWhiteSpace(providerConfig.ModelName)
                    ? null
                    : providerConfig.ModelName
            },
            services);
    }

    public static AudioRuntimeAttachmentOptions UseSpeechToTextProvider(
        this AudioRuntimeAttachmentOptions audio,
        IProviderRegistry providerRegistry,
        InputMediaSpeechToTextProviderOptions options,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(providerRegistry);
        ArgumentNullException.ThrowIfNull(options);

        var providerConfig = ResolveProviderConfig(options);
        var sessionOptions = new MeaiBatchSpeechToTextInteractionSessionOptions
        {
            ProviderKey = providerConfig.ProviderKey,
            ModelId = options.ModelId ?? EmptyAsNull(providerConfig.ModelName),
            SpeechLanguage = options.SpeechLanguage,
            TextLanguage = options.TextLanguage,
            SpeechSampleRate = options.SpeechSampleRate,
            Prompt = options.Prompt,
            Temperature = options.Temperature,
            ResponseFormat = options.ResponseFormat,
            TimestampGranularities = options.TimestampGranularities,
            IncludeLogprobs = options.IncludeLogprobs,
            AdditionalProperties = options.AdditionalProperties,
            RawRepresentationFactory = options.RawRepresentationFactory,
            TreatEmptyTranscriptAsError = options.TreatEmptyTranscriptAsError,
            DisposeClient = false
        };

        audio.InteractionSessionFactoryResolver = sourceResolver =>
            CreateFactory(
                providerRegistry,
                providerConfig,
                sourceResolver,
                sessionOptions,
                options.DisposeCreatedClient,
                services);

        return audio;
    }

    private static ProviderRegistrySpeechToTextInteractionSessionFactory CreateFactory(
        IProviderRegistry providerRegistry,
        ClientProviderConfig providerConfig,
        IInputContentSourceResolver sourceResolver,
        MeaiBatchSpeechToTextInteractionSessionOptions sessionOptions,
        bool disposeCreatedClient,
        IServiceProvider? services)
        => new(
            providerRegistry,
            providerConfig,
            sourceResolver,
            sessionOptions,
            disposeCreatedClient,
            services);

    private static ClientProviderConfig ResolveProviderConfig(
        InputMediaSpeechToTextProviderOptions options)
    {
        var config = Clone(options.ProviderConfig);
        if (!string.IsNullOrWhiteSpace(options.ProviderKey))
        {
            config.ProviderKey = options.ProviderKey;
        }

        if (!string.IsNullOrWhiteSpace(options.ModelId))
        {
            config.ModelName = options.ModelId;
        }

        if (string.IsNullOrWhiteSpace(config.ProviderKey))
        {
            throw new ArgumentException(
                "Input media speech-to-text provider configuration requires a ProviderKey.",
                nameof(options));
        }

        return config;
    }

    private static ClientProviderConfig Clone(ClientProviderConfig source)
        => new()
        {
            ProviderKey = source.ProviderKey,
            ModelName = source.ModelName,
            ApiKey = source.ApiKey,
            Endpoint = source.Endpoint,
            DefaultChatOptions = source.DefaultChatOptions,
            CustomHeaders = source.CustomHeaders is null
                ? null
                : new Dictionary<string, string>(source.CustomHeaders, StringComparer.OrdinalIgnoreCase),
            AdditionalProperties = source.AdditionalProperties is null
                ? null
                : new Dictionary<string, object>(source.AdditionalProperties),
            ProviderOptionsJson = source.ProviderOptionsJson,
            HttpReferer = source.HttpReferer,
            AppName = source.AppName,
            PromptFormatter = source.PromptFormatter
        };

    private static string? EmptyAsNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

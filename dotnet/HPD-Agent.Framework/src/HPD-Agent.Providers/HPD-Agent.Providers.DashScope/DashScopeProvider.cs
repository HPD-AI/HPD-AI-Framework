using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Cnblogs.DashScope.Core;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// DashScope provider implementation using the Cnblogs DashScope Microsoft.Extensions.AI adapter.
/// </summary>
internal class DashScopeProvider : IChatClientProvider, IEmbeddingGeneratorProvider
{
    private const string DefaultBaseAddress = "https://dashscope.aliyuncs.com/api/v1/";
    private const string DefaultWebsocketBaseAddress = "wss://dashscope.aliyuncs.com/api-ws/v1/inference/";

    public string ProviderKey => "dashscope";
    public string DisplayName => "DashScope";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in DashScopeProviderModule.")]
    public Meai.IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For DashScope, the ModelName must be configured.");
        }

        var dashScopeConfig = config.GetProviderConfig<DashScopeProviderConfig>();
        var client = CreateDashScopeClient(config, dashScopeConfig, services);
        var chatClient = client.AsChatClient(config.ModelName, dashScopeConfig?.UseVl);

        return new DashScopeConfiguredChatClient(chatClient, config.ModelName, dashScopeConfig);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in DashScopeProviderModule.")]
    public Meai.IEmbeddingGenerator CreateEmbeddingGenerator(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var dashScopeConfig = config.GetProviderConfig<DashScopeProviderConfig>(ProviderClientFamily.Embeddings)
            ?? config.GetProviderConfig<DashScopeProviderConfig>();

        var modelName =
            !string.IsNullOrWhiteSpace(config.ModelName) ? config.ModelName :
            !string.IsNullOrWhiteSpace(dashScopeConfig?.EmbeddingModelId) ? dashScopeConfig.EmbeddingModelId :
            "text-embedding-v4";

        var client = CreateDashScopeClient(config, dashScopeConfig, services);
        var generator = client.AsEmbeddingGenerator(modelName, dashScopeConfig?.EmbeddingDimensions);

        return new DashScopeConfiguredEmbeddingGenerator(generator, modelName, dashScopeConfig?.EmbeddingDimensions);
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new DashScopeErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://help.aliyun.com/zh/model-studio/"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    DefaultModelId = "qwen-plus",
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsReasoning"] = true,
                        ["SupportsVision"] = true,
                        ["VisionRequiresUseVl"] = "Auto-detected for qwen-vl/qwen3-vl/qwen3-omni/gui-plus models, or set DashScopeProviderConfig.UseVl."
                    }
                },
                [ProviderClientFamily.Embeddings] = new()
                {
                    Family = ProviderClientFamily.Embeddings,
                    DefaultModelId = "text-embedding-v4"
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in DashScopeProviderModule.")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family == ProviderClientFamily.Chat && string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add("Model name is required for DashScope");
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            errors.Add("API key is required for DashScope. " +
                       "Set it via the apiKey parameter, DASHSCOPE_API_KEY, QWEN_API_KEY, DASHSCOPE_KEY environment variable, or configuration.");
        }

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        var dashScopeConfig = config.GetProviderConfig<DashScopeProviderConfig>(family);
        if (dashScopeConfig is not null)
        {
            ValidateProviderOptions(dashScopeConfig, errors);
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(DashScopeProviderConfig config, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(config.BaseAddress) &&
            !Uri.IsWellFormedUriString(config.BaseAddress, UriKind.Absolute))
            errors.Add("BaseAddress must be a valid, absolute URI");

        if (!string.IsNullOrWhiteSpace(config.WebsocketBaseAddress) &&
            !Uri.IsWellFormedUriString(config.WebsocketBaseAddress, UriKind.Absolute))
            errors.Add("WebsocketBaseAddress must be a valid, absolute URI");

        if (config.SocketPoolSize.HasValue && config.SocketPoolSize.Value <= 0)
            errors.Add("SocketPoolSize must be greater than 0");

        if (config.TimeoutSeconds.HasValue && config.TimeoutSeconds.Value <= 0)
            errors.Add("TimeoutSeconds must be greater than 0");

        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
            errors.Add("Temperature must be between 0 and 2");

        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
            errors.Add("TopP must be between 0 and 1");

        if (config.TopK.HasValue && config.TopK.Value <= 0)
            errors.Add("TopK must be greater than 0");

        if (config.MaxOutputTokens.HasValue && config.MaxOutputTokens.Value <= 0)
            errors.Add("MaxOutputTokens must be greater than 0");

        if (config.Seed.HasValue && config.Seed.Value < 0)
            errors.Add("Seed must be greater than or equal to 0");

        if (config.StopSequences is { Count: > 0 })
        {
            foreach (var stopSequence in config.StopSequences)
            {
                if (string.IsNullOrEmpty(stopSequence))
                    errors.Add("StopSequences cannot contain empty values");
            }
        }

        if (config.EmbeddingModelId is { Length: 0 })
            errors.Add("EmbeddingModelId cannot be empty");

        if (config.EmbeddingDimensions.HasValue && config.EmbeddingDimensions.Value <= 0)
            errors.Add("EmbeddingDimensions must be greater than 0");
    }

    private static DashScopeClient CreateDashScopeClient(
        ClientProviderConfig config,
        DashScopeProviderConfig? dashScopeConfig,
        IServiceProvider? services)
    {
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var apiKeyTask = secrets.RequireAsync("dashscope:ApiKey", "DashScope", config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();

        var baseAddress =
            FirstNonWhiteSpace(config.Endpoint, dashScopeConfig?.BaseAddress) ?? DefaultBaseAddress;
        var websocketBaseAddress =
            string.IsNullOrWhiteSpace(dashScopeConfig?.WebsocketBaseAddress)
                ? DefaultWebsocketBaseAddress
                : dashScopeConfig.WebsocketBaseAddress!;

        var timeout = dashScopeConfig?.TimeoutSeconds is { } seconds
            ? TimeSpan.FromSeconds(seconds)
            : (TimeSpan?)null;

        return new DashScopeClient(
            apiKey,
            timeout,
            baseAddress,
            websocketBaseAddress,
            dashScopeConfig?.WorkspaceId,
            dashScopeConfig?.SocketPoolSize ?? 32);
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private sealed class DashScopeConfiguredChatClient : Meai.IChatClient
    {
        private readonly Meai.IChatClient _innerClient;
        private readonly string _modelName;
        private readonly DashScopeProviderConfig? _config;
        private Meai.ChatClientMetadata? _metadata;

        public DashScopeConfiguredChatClient(Meai.IChatClient innerClient, string modelName, DashScopeProviderConfig? config)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _config = config;
        }

        public Meai.ChatClientMetadata Metadata =>
            _metadata ??= new Meai.ChatClientMetadata("dashscope", defaultModelId: _modelName);

        public void Dispose() => _innerClient.Dispose();

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(Meai.ChatClientMetadata))
                return Metadata;

            return _innerClient.GetService(serviceType, serviceKey);
        }

        public System.Threading.Tasks.Task<Meai.ChatResponse> GetResponseAsync(
            IEnumerable<Meai.ChatMessage> messages,
            Meai.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => _innerClient.GetResponseAsync(messages, ApplyDefaults(options), cancellationToken);

        public IAsyncEnumerable<Meai.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Meai.ChatMessage> messages,
            Meai.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => _innerClient.GetStreamingResponseAsync(messages, ApplyDefaults(options), cancellationToken);

        private Meai.ChatOptions ApplyDefaults(Meai.ChatOptions? options)
        {
            if (options is null)
            {
                return CreateDefaultOptions();
            }

            return new Meai.ChatOptions
            {
                ModelId = string.IsNullOrWhiteSpace(options.ModelId) ? _modelName : options.ModelId,
                Instructions = options.Instructions,
                Tools = options.Tools,
                AllowMultipleToolCalls = options.AllowMultipleToolCalls,
                MaxOutputTokens = options.MaxOutputTokens ?? _config?.MaxOutputTokens,
                Temperature = options.Temperature ?? ToSingle(_config?.Temperature),
                TopP = options.TopP ?? ToSingle(_config?.TopP),
                TopK = options.TopK ?? _config?.TopK,
                FrequencyPenalty = options.FrequencyPenalty,
                PresencePenalty = options.PresencePenalty,
                StopSequences = options.StopSequences ?? _config?.StopSequences,
                ResponseFormat = options.ResponseFormat,
                Seed = options.Seed ?? _config?.Seed,
                ToolMode = options.ToolMode,
                Reasoning = options.Reasoning,
                AdditionalProperties = options.AdditionalProperties,
                RawRepresentationFactory = options.RawRepresentationFactory
            };
        }

        private Meai.ChatOptions CreateDefaultOptions() => new()
        {
            ModelId = _modelName,
            MaxOutputTokens = _config?.MaxOutputTokens,
            Temperature = ToSingle(_config?.Temperature),
            TopP = ToSingle(_config?.TopP),
            TopK = _config?.TopK,
            Seed = _config?.Seed,
            StopSequences = _config?.StopSequences
        };

        private static float? ToSingle(double? value) => value.HasValue ? (float)value.Value : null;
    }

    private sealed class DashScopeConfiguredEmbeddingGenerator : Meai.IEmbeddingGenerator
    {
        private readonly Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> _innerGenerator;
        private readonly string _modelName;
        private readonly int? _dimensions;
        private Meai.EmbeddingGeneratorMetadata? _metadata;

        public DashScopeConfiguredEmbeddingGenerator(
            Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> innerGenerator,
            string modelName,
            int? dimensions)
        {
            _innerGenerator = innerGenerator ?? throw new ArgumentNullException(nameof(innerGenerator));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _dimensions = dimensions;
        }

        public Meai.EmbeddingGeneratorMetadata Metadata =>
            _metadata ??= new Meai.EmbeddingGeneratorMetadata("dashscope", defaultModelId: _modelName);

        public void Dispose() => _innerGenerator.Dispose();

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(Meai.EmbeddingGeneratorMetadata))
                return Metadata;

            return _innerGenerator.GetService(serviceType, serviceKey);
        }

        public async System.Threading.Tasks.Task<Meai.GeneratedEmbeddings<Meai.Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            Meai.EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new Meai.EmbeddingGenerationOptions();
            if (string.IsNullOrWhiteSpace(options.ModelId))
            {
                options.ModelId = _modelName;
            }

            options.Dimensions ??= _dimensions;

            return await _innerGenerator.GenerateAsync(values, options, cancellationToken).ConfigureAwait(false);
        }
    }
}

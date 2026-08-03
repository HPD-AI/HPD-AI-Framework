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
    public async ValueTask<Meai.IChatClient> CreateChatClientAsync(ClientProviderConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For DashScope, the ModelName must be configured.");
        }

        var dashScopeConfig = config.GetProviderConfig<DashScopeProviderConfig>();
        var client = CreateDashScopeClient(config, dashScopeConfig, services);
        var chatClient = client.AsChatClient(config.ModelName, dashScopeConfig?.DefaultUseVl);

        return new DashScopeConfiguredChatClient(chatClient, config.ModelName, dashScopeConfig?.DefaultUseVl);
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
                        ["VisionRequiresUseVl"] = "Auto-detected for qwen-vl/qwen3-vl/qwen3-omni/gui-plus models, or set DashScopeProviderConfig.DefaultUseVl."
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
        private readonly bool? _useVl;
        private Meai.ChatClientMetadata? _metadata;

        public DashScopeConfiguredChatClient(Meai.IChatClient innerClient, string modelName, bool? useVl)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _useVl = useVl;
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
            var merged = options?.Clone() ?? new Meai.ChatOptions();
            if (string.IsNullOrWhiteSpace(merged.ModelId))
                merged.ModelId = _modelName;

            DashScopeChatRequestOptionKeys.ApplyRawParameters(merged, _modelName, _useVl);

            return merged;
        }
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

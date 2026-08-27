using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Text.Json;
using Cnblogs.DashScope.Core;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// DashScope provider implementation using the Cnblogs DashScope Microsoft.Extensions.AI adapter.
/// </summary>
[HpdProvider("dashscope", "DashScope")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "dashscope:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderFamily(ProviderClientFamily.Embeddings)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(DashScopeProviderConfig), typeof(DashScopeJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(DashScopeChatRequestOptions), typeof(DashScopeJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Embeddings, ProviderPayloadKind.Configuration, typeof(DashScopeProviderConfig), typeof(DashScopeJsonContext))]
[HpdProviderSecretAlias("dashscope:ApiKey", "DASHSCOPE_API_KEY", "QWEN_API_KEY", "DASHSCOPE_KEY")]
internal class DashScopeProvider : IProvider,
    IProviderClientFactory<Meai.IChatClient>,
    IProviderClientFactory<Meai.IEmbeddingGenerator>,
    IProviderSecretAliasProvider
{
    private const string DefaultBaseAddress = "https://dashscope.aliyuncs.com/api/v1/";
    private const string DefaultWebsocketBaseAddress = "wss://dashscope.aliyuncs.com/api-ws/v1/inference/";

    public string ProviderKey => "dashscope";
    public string DisplayName => "DashScope";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("dashscope:ApiKey", new[] { "DASHSCOPE_API_KEY", "QWEN_API_KEY", "DASHSCOPE_KEY" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    ProviderClientCredentialBinding IProviderClientFactory<Meai.IChatClient>.ResolveCredentialBinding(
        ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<Meai.IEmbeddingGenerator>.ResolveCredentialBinding(
        ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);

    ValueTask<ProviderClientConstruction<Meai.IChatClient>> IProviderClientFactory<Meai.IChatClient>.CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For DashScope, the ModelName must be configured.");
        }

        var dashScopeConfig = ReadConfig(config);
        var options = config.FamilyOperation.CanonicalPayload.IsEmpty ? null : JsonSerializer.Deserialize(
            config.FamilyOperation.CanonicalPayload.AsSpan(), DashScopeJsonContext.Default.DashScopeChatRequestOptions);
        var useVl = options?.UseVl;
        var client = CreateDashScopeClient(context, dashScopeConfig);
        var chatClient = client.AsChatClient(config.ModelName, useVl);

        Meai.IChatClient configured = new DashScopeConfiguredChatClient(chatClient, config.ModelName, useVl);
        return ValueTask.FromResult(new ProviderClientConstruction<Meai.IChatClient>
        {
            Client = configured,
            Owner = ProviderClientConstructionUtilities.Own(configured)
        });
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    ValueTask<ProviderClientConstruction<Meai.IEmbeddingGenerator>> IProviderClientFactory<Meai.IEmbeddingGenerator>.CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;

        var dashScopeConfig = ReadConfig(config);

        var modelName =
            !string.IsNullOrWhiteSpace(config.ModelName) ? config.ModelName :
            "text-embedding-v4";

        int? dimensions = null;

        var client = CreateDashScopeClient(context, dashScopeConfig);
        var generator = client.AsEmbeddingGenerator(modelName, dimensions);

        Meai.IEmbeddingGenerator configured = new DashScopeConfiguredEmbeddingGenerator(generator, modelName, dimensions);
        return ValueTask.FromResult(new ProviderClientConstruction<Meai.IEmbeddingGenerator>
        {
            Client = configured,
            Owner = ProviderClientConstructionUtilities.Own(configured)
        });
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
                        ["VisionRequiresUseVl"] = "Auto-detected from the model, or set DashScopeChatRequestOptions.UseVl."
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (config.Family == ProviderClientFamily.Chat && string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add("Model name is required for DashScope");
        }


        var dashScopeConfig = ReadConfig(config);
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
        if (!string.IsNullOrWhiteSpace(config.WebsocketBaseAddress) &&
            !Uri.IsWellFormedUriString(config.WebsocketBaseAddress, UriKind.Absolute))
            errors.Add("WebsocketBaseAddress must be a valid, absolute URI");

        if (config.SocketPoolSize.HasValue && config.SocketPoolSize.Value <= 0)
            errors.Add("SocketPoolSize must be greater than 0");

        if (config.TimeoutSeconds.HasValue && config.TimeoutSeconds.Value <= 0)
            errors.Add("TimeoutSeconds must be greater than 0");

    }

    private static ProviderClientCredentialBinding ResolveBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    private static DashScopeClient CreateDashScopeClient(
        ProviderClientConstructionContext context,
        DashScopeProviderConfig? dashScopeConfig)
    {
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);

        var baseAddress = context.EffectiveConfig.Endpoint?.AbsoluteUri ?? DefaultBaseAddress;
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

    private static DashScopeProviderConfig? ReadConfig(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty ? null : JsonSerializer.Deserialize(
            config.ProviderConfiguration.CanonicalPayload.AsSpan(), DashScopeJsonContext.Default.DashScopeProviderConfig);

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

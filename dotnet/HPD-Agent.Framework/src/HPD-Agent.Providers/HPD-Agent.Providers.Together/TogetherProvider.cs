using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// Together AI provider implementation using the tryAGI Together SDK.
/// </summary>
[HpdProvider("together", "Together AI")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "together:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderFamily(ProviderClientFamily.Embeddings)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(TogetherChatRequestOptions), typeof(TogetherJsonContext))]
[HpdProviderSecretAlias("together:ApiKey", "TOGETHER_API_KEY")]
internal class TogetherProvider : IProvider,
    IProviderClientFactory<Meai.IChatClient>,
    IProviderClientFactory<Meai.IEmbeddingGenerator>,
    IProviderSecretAliasProvider
{
    private static readonly Uri DefaultEndpoint = new("https://api.together.ai/v1");
    private const string DefaultChatModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo";
    private const string DefaultEmbeddingModel = "BAAI/bge-base-en-v1.5";

    public string ProviderKey => "together";
    public string DisplayName => "Together AI";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("together:ApiKey", new[] { "TOGETHER_API_KEY" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    ProviderClientCredentialBinding IProviderClientFactory<Meai.IChatClient>.ResolveCredentialBinding(
        ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);

    ProviderClientCredentialBinding IProviderClientFactory<Meai.IEmbeddingGenerator>.ResolveCredentialBinding(
        ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);

    ValueTask<ProviderClientConstruction<Meai.IChatClient>> IProviderClientFactory<Meai.IChatClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For Together AI, the ModelName must be configured.");
        }

        var client = CreateTogetherClient(context);
        Meai.IChatClient configured = new TogetherConfiguredChatClient(client, config.ModelName);

        return ValueTask.FromResult(new ProviderClientConstruction<Meai.IChatClient>
        {
            Client = configured,
            Owner = ProviderClientConstructionUtilities.Own(configured)
        });
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    ValueTask<ProviderClientConstruction<Meai.IEmbeddingGenerator>> IProviderClientFactory<Meai.IEmbeddingGenerator>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;

        var client = CreateTogetherClient(context);
        var modelName =
            !string.IsNullOrWhiteSpace(config.ModelName) ? config.ModelName :
            DefaultEmbeddingModel;

        Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> generator = client;
        Meai.IEmbeddingGenerator configured = new TogetherConfiguredEmbeddingGenerator(generator, modelName);
        return ValueTask.FromResult(new ProviderClientConstruction<Meai.IEmbeddingGenerator>
        {
            Client = configured,
            Owner = ProviderClientConstructionUtilities.Own(configured)
        });
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new TogetherErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://docs.together.ai/"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    DefaultModelId = DefaultChatModel,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = false,
                        ["SupportsReasoning"] = true,
                        ["SupportsJsonResponseFormat"] = true
                    }
                },
                [ProviderClientFamily.Embeddings] = new()
                {
                    Family = ProviderClientFamily.Embeddings,
                    DefaultModelId = DefaultEmbeddingModel
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
            errors.Add("Model name is required for Together AI");
        }


        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static ProviderClientCredentialBinding ResolveBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    private static global::Together.TogetherClient CreateTogetherClient(ProviderClientConstructionContext context)
    {
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var config = context.EffectiveConfig;

        var endpoint = config.Endpoint ?? DefaultEndpoint;

        var client = new global::Together.TogetherClient(baseUri: endpoint);
        client.AuthorizeUsingBearer(apiKey);
        return client;
    }

    private sealed class TogetherConfiguredChatClient : Meai.IChatClient
    {
        private readonly Meai.IChatClient _innerClient;
        private readonly string _modelName;
        private Meai.ChatClientMetadata? _metadata;

        public TogetherConfiguredChatClient(Meai.IChatClient innerClient, string modelName)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        }

        public Meai.ChatClientMetadata Metadata =>
            _metadata ??= new Meai.ChatClientMetadata("together", defaultModelId: _modelName);

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

            TogetherChatRequestOptionKeys.ApplyRawRequestOptions(merged);

            return merged;
        }
    }

    private sealed class TogetherConfiguredEmbeddingGenerator : Meai.IEmbeddingGenerator
    {
        private readonly Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> _innerGenerator;
        private readonly string _modelName;
        private Meai.EmbeddingGeneratorMetadata? _metadata;

        public TogetherConfiguredEmbeddingGenerator(
            Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> innerGenerator,
            string modelName)
        {
            _innerGenerator = innerGenerator ?? throw new ArgumentNullException(nameof(innerGenerator));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        }

        public Meai.EmbeddingGeneratorMetadata Metadata =>
            _metadata ??= new Meai.EmbeddingGeneratorMetadata("together", defaultModelId: _modelName);

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

            return await _innerGenerator.GenerateAsync(values, options, cancellationToken).ConfigureAwait(false);
        }
    }
}

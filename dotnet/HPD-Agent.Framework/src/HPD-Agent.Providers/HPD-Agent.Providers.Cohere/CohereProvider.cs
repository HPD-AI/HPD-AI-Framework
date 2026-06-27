using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Cohere;

/// <summary>
/// Cohere provider implementation using the tryAGI Cohere SDK.
/// </summary>
internal class CohereProvider : IChatClientProvider, IEmbeddingGeneratorProvider
{
    private static readonly Uri DefaultEndpoint = new("https://api.cohere.com/");

    public string ProviderKey => "cohere";
    public string DisplayName => "Cohere";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in CohereProviderModule.")]
    public Meai.IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For Cohere, the ModelName must be configured.");
        }

        var client = CreateCohereClient(config, services);
        var cohereConfig = config.GetProviderConfig<CohereProviderConfig>();

        return new CohereConfiguredChatClient(client, config.ModelName, cohereConfig);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in CohereProviderModule.")]
    public Meai.IEmbeddingGenerator CreateEmbeddingGenerator(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var client = CreateCohereClient(config, services);
        var cohereConfig = config.GetProviderConfig<CohereProviderConfig>(ProviderClientFamily.Embeddings)
            ?? config.GetProviderConfig<CohereProviderConfig>();

        var modelName =
            !string.IsNullOrWhiteSpace(config.ModelName) ? config.ModelName :
            !string.IsNullOrWhiteSpace(cohereConfig?.EmbeddingModelId) ? cohereConfig.EmbeddingModelId :
            "embed-english-v3.0";

        Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> generator = client;
        return new CohereConfiguredEmbeddingGenerator(generator, modelName);
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new CohereErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://docs.cohere.com/"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    DefaultModelId = "command-r-plus",
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["StreamingMode"] = "SingleFinalUpdate",
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = false
                    }
                },
                [ProviderClientFamily.Embeddings] = new()
                {
                    Family = ProviderClientFamily.Embeddings,
                    DefaultModelId = "embed-english-v3.0"
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in CohereProviderModule.")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family == ProviderClientFamily.Chat && string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add("Model name is required for Cohere");
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            errors.Add("API key is required for Cohere. " +
                       "Set it via the apiKey parameter, COHERE_API_KEY environment variable, or configuration.");
        }

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        var cohereConfig = config.GetProviderConfig<CohereProviderConfig>(family);
        if (cohereConfig is not null)
        {
            ValidateProviderOptions(cohereConfig, errors);
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(CohereProviderConfig config, List<string> errors)
    {
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 5))
            errors.Add("Temperature must be between 0 and 5");

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
    }

    private static global::Cohere.CohereClient CreateCohereClient(ClientProviderConfig config, IServiceProvider? services)
    {
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var apiKeyTask = secrets.RequireAsync("cohere:ApiKey", "Cohere", config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();

        var endpoint = string.IsNullOrWhiteSpace(config.Endpoint)
            ? DefaultEndpoint
            : new Uri(config.Endpoint, UriKind.Absolute);

        return new global::Cohere.CohereClient(apiKey, baseUri: endpoint);
    }

    private sealed class CohereConfiguredChatClient : Meai.IChatClient
    {
        private readonly Meai.IChatClient _innerClient;
        private readonly string _modelName;
        private readonly CohereProviderConfig? _config;
        private Meai.ChatClientMetadata? _metadata;

        public CohereConfiguredChatClient(Meai.IChatClient innerClient, string modelName, CohereProviderConfig? config)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _config = config;
        }

        public Meai.ChatClientMetadata Metadata =>
            _metadata ??= new Meai.ChatClientMetadata("cohere", defaultModelId: _modelName);

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

    private sealed class CohereConfiguredEmbeddingGenerator : Meai.IEmbeddingGenerator
    {
        private readonly Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> _innerGenerator;
        private readonly string _modelName;
        private Meai.EmbeddingGeneratorMetadata? _metadata;

        public CohereConfiguredEmbeddingGenerator(
            Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> innerGenerator,
            string modelName)
        {
            _innerGenerator = innerGenerator ?? throw new ArgumentNullException(nameof(innerGenerator));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        }

        public Meai.EmbeddingGeneratorMetadata Metadata =>
            _metadata ??= new Meai.EmbeddingGeneratorMetadata("cohere", defaultModelId: _modelName);

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

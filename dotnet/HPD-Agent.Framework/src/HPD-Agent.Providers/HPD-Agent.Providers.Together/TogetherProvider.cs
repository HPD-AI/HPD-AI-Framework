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

namespace HPD.Agent.Providers.Together;

/// <summary>
/// Together AI provider implementation using the tryAGI Together SDK.
/// </summary>
internal class TogetherProvider : IChatClientProvider, IEmbeddingGeneratorProvider
{
    private static readonly Uri DefaultEndpoint = new("https://api.together.ai/v1");
    private const string DefaultChatModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo";
    private const string DefaultEmbeddingModel = "BAAI/bge-base-en-v1.5";

    public string ProviderKey => "together";
    public string DisplayName => "Together AI";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in TogetherProviderModule.")]
    public Meai.IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For Together AI, the ModelName must be configured.");
        }

        var client = CreateTogetherClient(config, services);
        var togetherConfig = config.GetProviderConfig<TogetherProviderConfig>();

        return new TogetherConfiguredChatClient(client, config.ModelName, togetherConfig);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in TogetherProviderModule.")]
    public Meai.IEmbeddingGenerator CreateEmbeddingGenerator(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var client = CreateTogetherClient(config, services);
        var togetherConfig = config.GetProviderConfig<TogetherProviderConfig>(ProviderClientFamily.Embeddings)
            ?? config.GetProviderConfig<TogetherProviderConfig>();

        var modelName =
            !string.IsNullOrWhiteSpace(config.ModelName) ? config.ModelName :
            !string.IsNullOrWhiteSpace(togetherConfig?.EmbeddingModelId) ? togetherConfig.EmbeddingModelId :
            DefaultEmbeddingModel;

        Meai.IEmbeddingGenerator<string, Meai.Embedding<float>> generator = client;
        return new TogetherConfiguredEmbeddingGenerator(generator, modelName);
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in TogetherProviderModule.")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family == ProviderClientFamily.Chat && string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add("Model name is required for Together AI");
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            errors.Add("API key is required for Together AI. " +
                       "Set it via the apiKey parameter, TOGETHER_API_KEY environment variable, or configuration.");
        }

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        var togetherConfig = config.GetProviderConfig<TogetherProviderConfig>(family);
        if (togetherConfig is not null)
        {
            ValidateProviderOptions(togetherConfig, errors);
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(TogetherProviderConfig config, List<string> errors)
    {
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
            errors.Add("Temperature must be between 0 and 2");

        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
            errors.Add("TopP must be between 0 and 1");

        if (config.TopK.HasValue && config.TopK.Value <= 0)
            errors.Add("TopK must be greater than 0");

        if (config.MaxOutputTokens.HasValue && config.MaxOutputTokens.Value <= 0)
            errors.Add("MaxOutputTokens must be greater than 0");

        if (config.Seed.HasValue && config.Seed.Value is < int.MinValue or > int.MaxValue)
            errors.Add("Seed must fit in a 32-bit signed integer");

        if (config.StopSequences is { Count: > 0 })
        {
            foreach (var stopSequence in config.StopSequences)
            {
                if (string.IsNullOrEmpty(stopSequence))
                    errors.Add("StopSequences cannot contain empty values");
            }
        }

        if (config.ResponseFormat is { Length: > 0 } responseFormat)
        {
            if (!string.Equals(responseFormat, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(responseFormat, "json_object", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("ResponseFormat must be one of: text, json_object");
            }
        }

        if (config.EmbeddingModelId is { Length: 0 })
            errors.Add("EmbeddingModelId cannot be empty");
    }

    private static global::Together.TogetherClient CreateTogetherClient(ClientProviderConfig config, IServiceProvider? services)
    {
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var apiKeyTask = secrets.RequireAsync("together:ApiKey", "Together AI", config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();

        var endpoint = string.IsNullOrWhiteSpace(config.Endpoint)
            ? DefaultEndpoint
            : new Uri(config.Endpoint, UriKind.Absolute);

        var client = new global::Together.TogetherClient(baseUri: endpoint);
        client.AuthorizeUsingBearer(apiKey);
        return client;
    }

    private sealed class TogetherConfiguredChatClient : Meai.IChatClient
    {
        private readonly Meai.IChatClient _innerClient;
        private readonly string _modelName;
        private readonly TogetherProviderConfig? _config;
        private Meai.ChatClientMetadata? _metadata;

        public TogetherConfiguredChatClient(Meai.IChatClient innerClient, string modelName, TogetherProviderConfig? config)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _config = config;
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
                ResponseFormat = options.ResponseFormat ?? CreateResponseFormat(_config?.ResponseFormat),
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
            StopSequences = _config?.StopSequences,
            ResponseFormat = CreateResponseFormat(_config?.ResponseFormat)
        };

        private static Meai.ChatResponseFormat? CreateResponseFormat(string? responseFormat)
            => string.Equals(responseFormat, "json_object", StringComparison.OrdinalIgnoreCase)
                ? Meai.ChatResponseFormat.Json
                : null;

        private static float? ToSingle(double? value) => value.HasValue ? (float)value.Value : null;
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

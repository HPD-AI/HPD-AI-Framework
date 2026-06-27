using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Fireworks;

internal class FireworksProvider : IChatClientProvider
{
    private static readonly Uri DefaultEndpoint = new("https://api.fireworks.ai/inference/v1/");
    private const string DefaultChatModel = "accounts/fireworks/models/llama-v3p1-8b-instruct";

    public string ProviderKey => "fireworks";
    public string DisplayName => "Fireworks AI";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in FireworksProviderModule.")]
    public Meai.IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For Fireworks AI, the ModelName must be configured.");
        }

        var secrets = services?.GetService<ISecretResolver>();
        if (secrets is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var apiKeyTask = secrets.RequireAsync("fireworks:ApiKey", DisplayName, config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();

        var endpointTask = secrets.ResolveOrDefaultAsync("fireworks:Endpoint", config.Endpoint, CancellationToken.None);
        var endpoint = endpointTask.GetAwaiter().GetResult();

        var baseUri = string.IsNullOrWhiteSpace(endpoint)
            ? DefaultEndpoint
            : new Uri(endpoint, UriKind.Absolute);

        var httpClient = new HttpClient
        {
            BaseAddress = baseUri
        };
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var client = new OpenAICompatibleChatClient(httpClient, new OpenAICompatibleChatClientOptions
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            ProviderUri = baseUri,
            DefaultModelId = config.ModelName,
            ChatCompletionsPath = "chat/completions"
        });

        return new FireworksConfiguredChatClient(client, config.ModelName, baseUri, config.GetProviderConfig<FireworksProviderConfig>());
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new FireworksErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://docs.fireworks.ai/"),
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
                        ["SupportsJsonResponseFormat"] = true,
                        ["SupportsSeed"] = true
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in FireworksProviderModule.")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family != ProviderClientFamily.Chat)
        {
            errors.Add("Fireworks AI currently supports only the chat provider family.");
        }

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add("Model name is required for Fireworks AI");
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            errors.Add("API key is required for Fireworks AI. " +
                       "Set it via the apiKey parameter, FIREWORKS_API_KEY environment variable, or configuration.");
        }

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        var fireworksConfig = config.GetProviderConfig<FireworksProviderConfig>(family);
        if (fireworksConfig is not null)
        {
            ValidateProviderOptions(fireworksConfig, errors);
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(FireworksProviderConfig config, List<string> errors)
    {
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
            errors.Add("Temperature must be between 0 and 2");

        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
            errors.Add("TopP must be between 0 and 1");

        if (config.MaxOutputTokens.HasValue && config.MaxOutputTokens.Value <= 0)
            errors.Add("MaxOutputTokens must be greater than 0");

        if (config.StopSequences is { Count: > 0 })
        {
            foreach (var stopSequence in config.StopSequences)
            {
                if (string.IsNullOrEmpty(stopSequence))
                    errors.Add("StopSequences cannot contain empty values");
            }
        }

        if (config.ResponseFormat is { Length: > 0 } responseFormat &&
            !string.Equals(responseFormat, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(responseFormat, "json_object", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ResponseFormat must be one of: text, json_object");
        }

        if (config.ToolChoice is { Length: > 0 } toolChoice &&
            !string.Equals(toolChoice, "auto", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ToolChoice must be one of: auto, none, required");
        }
    }

    private sealed class FireworksConfiguredChatClient : Meai.IChatClient
    {
        private readonly Meai.IChatClient _innerClient;
        private readonly string _modelName;
        private readonly Uri _providerUri;
        private readonly FireworksProviderConfig? _config;
        private Meai.ChatClientMetadata? _metadata;

        public FireworksConfiguredChatClient(Meai.IChatClient innerClient, string modelName, Uri providerUri, FireworksProviderConfig? config)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _providerUri = providerUri ?? throw new ArgumentNullException(nameof(providerUri));
            _config = config;
        }

        public Meai.ChatClientMetadata Metadata =>
            _metadata ??= new Meai.ChatClientMetadata("fireworks", providerUri: _providerUri, defaultModelId: _modelName);

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
                FrequencyPenalty = options.FrequencyPenalty,
                PresencePenalty = options.PresencePenalty,
                StopSequences = options.StopSequences ?? _config?.StopSequences,
                ResponseFormat = options.ResponseFormat ?? CreateResponseFormat(_config?.ResponseFormat),
                Seed = options.Seed ?? _config?.Seed,
                ToolMode = options.ToolMode ?? CreateToolMode(_config?.ToolChoice),
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
            Seed = _config?.Seed,
            StopSequences = _config?.StopSequences,
            ResponseFormat = CreateResponseFormat(_config?.ResponseFormat),
            ToolMode = CreateToolMode(_config?.ToolChoice)
        };

        private static Meai.ChatResponseFormat? CreateResponseFormat(string? responseFormat)
            => string.Equals(responseFormat, "json_object", StringComparison.OrdinalIgnoreCase)
                ? Meai.ChatResponseFormat.Json
                : string.Equals(responseFormat, "text", StringComparison.OrdinalIgnoreCase)
                    ? Meai.ChatResponseFormat.Text
                    : null;

        private static Meai.ChatToolMode? CreateToolMode(string? toolChoice)
            => toolChoice?.ToLowerInvariant() switch
            {
                "none" => Meai.ChatToolMode.None,
                "required" => Meai.ChatToolMode.RequireAny,
                _ => null
            };

        private static float? ToSingle(double? value) => value.HasValue ? (float)value.Value : null;
    }
}

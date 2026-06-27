using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Groq;

/// <summary>
/// Groq provider implementation using the shared OpenAI-compatible chat completions client.
/// </summary>
internal sealed class GroqProvider : IChatClientProvider
{
    internal static readonly Uri DefaultEndpoint = new("https://api.groq.com/openai/v1/");
    internal const string DefaultChatModel = "llama-3.3-70b-versatile";

    public string ProviderKey => "groq";
    public string DisplayName => "Groq";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in GroqProviderModule.")]
    public Meai.IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For Groq, the ModelName must be configured.");
        }

        var secrets = services?.GetService<ISecretResolver>();
        if (secrets is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var apiKey = secrets.RequireAsync("groq:ApiKey", "Groq API Key", config.ApiKey, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var endpointValue = secrets.ResolveOrDefaultAsync("groq:Endpoint", config.Endpoint, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var endpoint = string.IsNullOrWhiteSpace(endpointValue)
            ? DefaultEndpoint
            : EnsureTrailingSlash(new Uri(endpointValue, UriKind.Absolute));

        var httpClient = new HttpClient
        {
            BaseAddress = endpoint
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        if (config.CustomHeaders is not null)
        {
            foreach (var header in config.CustomHeaders)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var groqConfig = config.GetProviderConfig<GroqProviderConfig>();
        var options = new OpenAICompatibleChatClientOptions
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            ProviderUri = new Uri("https://groq.com/"),
            DefaultModelId = config.ModelName,
            ChatCompletionsPath = "chat/completions"
        };

        return new GroqConfiguredChatClient(new OpenAICompatibleChatClient(httpClient, options), groqConfig);
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new GroqErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://console.groq.com/docs/"),
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
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in GroqProviderModule.")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family != ProviderClientFamily.Chat)
        {
            errors.Add("Groq currently supports the chat client family.");
        }

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add("Model name is required for Groq");
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            errors.Add("API key is required for Groq. " +
                       "Set it via the apiKey parameter, GROQ_API_KEY environment variable, or configuration.");
        }

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        var groqConfig = config.GetProviderConfig<GroqProviderConfig>(family);
        if (groqConfig is not null)
        {
            ValidateProviderOptions(groqConfig, errors);
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(GroqProviderConfig config, List<string> errors)
    {
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
            errors.Add("Temperature must be between 0 and 2");

        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
            errors.Add("TopP must be between 0 and 1");

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

        if (config.ToolChoice is { Length: > 0 } toolChoice)
        {
            if (!string.Equals(toolChoice, "auto", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("ToolChoice must be one of: auto, none, required");
            }
        }
    }

    private static Uri EnsureTrailingSlash(Uri endpoint)
    {
        if (endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            return endpoint;
        }

        return new Uri(endpoint.AbsoluteUri + "/", UriKind.Absolute);
    }

    private sealed class GroqConfiguredChatClient : Meai.IChatClient
    {
        private readonly OpenAICompatibleChatClient _innerClient;
        private readonly GroqProviderConfig? _config;

        public GroqConfiguredChatClient(
            OpenAICompatibleChatClient innerClient,
            GroqProviderConfig? config)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _config = config;
        }

        public void Dispose() => _innerClient.Dispose();

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(GroqProviderConfig))
                return _config;

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

        private Meai.ChatOptions? ApplyDefaults(Meai.ChatOptions? options)
        {
            if (_config is null)
            {
                return options;
            }

            if (options is null)
            {
                return CreateDefaultOptions();
            }

            return new Meai.ChatOptions
            {
                ModelId = options.ModelId,
                Instructions = options.Instructions,
                Tools = options.Tools,
                MaxOutputTokens = options.MaxOutputTokens ?? _config.MaxOutputTokens,
                Temperature = options.Temperature ?? ToSingle(_config.Temperature),
                TopP = options.TopP ?? ToSingle(_config.TopP),
                TopK = options.TopK,
                FrequencyPenalty = options.FrequencyPenalty,
                PresencePenalty = options.PresencePenalty,
                StopSequences = options.StopSequences ?? _config.StopSequences,
                ResponseFormat = options.ResponseFormat ?? CreateResponseFormat(_config.ResponseFormat),
                Seed = options.Seed ?? _config.Seed,
                ToolMode = options.ToolMode ?? CreateToolMode(_config.ToolChoice),
                AdditionalProperties = options.AdditionalProperties,
                RawRepresentationFactory = options.RawRepresentationFactory
            };
        }

        private Meai.ChatOptions CreateDefaultOptions() => new()
        {
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

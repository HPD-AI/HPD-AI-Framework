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

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxCompletionTokens,
        StopSequences = true,
        Seed = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        Tools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        RequiredToolChoice = true,
        NamedToolChoice = true,
        ParallelToolCalls = true,
        Vision = true
    };

    public string ProviderKey => "groq";
    public string DisplayName => "Groq";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in GroqProviderModule.")]
    public async ValueTask<Meai.IChatClient> CreateChatClientAsync(ClientProviderConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
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

        var apiKey = await secrets.RequireAsync("groq:ApiKey", "Groq API Key", config.ApiKey, cancellationToken).ConfigureAwait(false);
        var endpointValue = await secrets.ResolveOrDefaultAsync("groq:Endpoint", config.Endpoint, cancellationToken).ConfigureAwait(false);

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

        var options = new OpenAICompatibleChatClientOptions
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            ProviderUri = new Uri("https://groq.com/"),
            DefaultModelId = config.ModelName,
            ChatCompletionsPath = "chat/completions",
            RequestProfile = ChatRequestProfile
        };

        return new GroqConfiguredChatClient(new OpenAICompatibleChatClient(httpClient, options));
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
                        ["SupportsVision"] = true,
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


        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
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

        public GroqConfiguredChatClient(OpenAICompatibleChatClient innerClient)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        }

        public void Dispose() => _innerClient.Dispose();

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
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
            return options?.Clone() ?? new Meai.ChatOptions();
        }
    }
}

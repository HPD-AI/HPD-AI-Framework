using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAICompatible;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.DeepInfra;

[HpdProvider("deepinfra", "DeepInfra", DocumentationUrl = "https://docs.deepinfra.com/chat/overview")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "deepinfra:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(DeepInfraProviderConfig), typeof(DeepInfraJsonContext))]
[HpdProviderSecretAlias("deepinfra:ApiKey", "DEEPINFRA_API_KEY")]
[HpdProviderSecretAlias("deepinfra:Endpoint", "DEEPINFRA_ENDPOINT", "DEEPINFRA_BASE_URL")]
internal class DeepInfraProvider : IProvider, IProviderClientFactory<Meai.IChatClient>, IProviderSecretAliasProvider
{
    internal static readonly Uri DefaultEndpoint = new("https://api.deepinfra.com/v1/openai/");
    internal const string DefaultChatModel = "meta-llama/Meta-Llama-3-8B-Instruct";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        FrequencyPenalty = true,
        PresencePenalty = true,
        StopSequences = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        StrictJsonSchema = true,
        Tools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        ParallelToolCalls = true,
        Vision = true,
        ApplyReasoning = ApplyReasoning
    };

    public string ProviderKey => "deepinfra";
    public string DisplayName => "DeepInfra";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("deepinfra:ApiKey", new[] { "DEEPINFRA_API_KEY" }),
            new("deepinfra:Endpoint", new[] { "DEEPINFRA_ENDPOINT", "DEEPINFRA_BASE_URL" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public ValueTask<ProviderClientConstruction<Meai.IChatClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For DeepInfra, the ModelName must be configured.");
        }

        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var baseAddress = config.Endpoint ?? DefaultEndpoint;

        var httpClient = context.Services.HttpClientFactory.CreateClient("hpd-provider-deepinfra");
        httpClient.BaseAddress = EnsureTrailingSlash(baseAddress);
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var client = new OpenAICompatibleChatClient(
            httpClient,
            new OpenAICompatibleChatClientOptions
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName,
                ProviderUri = new Uri("https://deepinfra.com/"),
                DefaultModelId = config.ModelName,
                ChatCompletionsPath = "chat/completions",
                RequestProfile = ChatRequestProfile
            });

        Meai.IChatClient configured = new DeepInfraConfiguredChatClient(client, config.ModelName);
        return ValueTask.FromResult(new ProviderClientConstruction<Meai.IChatClient>
        {
            Client = configured,
            Owner = ProviderClientConstructionUtilities.Own(httpClient, configured)
        });
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new DeepInfraErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://docs.deepinfra.com/chat/overview"),
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
                        ["SupportsReasoning"] = true,
                        ["SupportsVision"] = true,
                        ["OpenAICompatibleEndpoint"] = "https://api.deepinfra.com/v1/openai/"
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (config.Family != ProviderClientFamily.Chat)
            errors.Add("DeepInfra supports only chat.");
        if (config.Family == ProviderClientFamily.Chat && string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add("Model name is required for DeepInfra");
        }


        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var text = uri.AbsoluteUri;
        return text.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(text + "/", UriKind.Absolute);
    }

    /// <summary>
    /// Maps MEAI reasoning levels to the efforts documented by DeepInfra.
    /// </summary>
    private static void ApplyReasoning(
        OpenAICompatibleChatRequest request,
        Meai.ReasoningOptions reasoning)
    {
        request.ReasoningEffort = reasoning.Effort switch
        {
            Meai.ReasoningEffort.None => "none",
            Meai.ReasoningEffort.Low => "low",
            Meai.ReasoningEffort.Medium => "medium",
            Meai.ReasoningEffort.High => "high",
            _ => null
        };
    }

    private sealed class DeepInfraConfiguredChatClient : Meai.IChatClient
    {
        private readonly Meai.IChatClient _innerClient;
        private readonly string _modelName;
        private Meai.ChatClientMetadata? _metadata;

        public DeepInfraConfiguredChatClient(Meai.IChatClient innerClient, string modelName)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        }

        public Meai.ChatClientMetadata Metadata =>
            _metadata ??= new Meai.ChatClientMetadata("deepinfra", DeepInfraProvider.DefaultEndpoint, _modelName);

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

            return merged;
        }
    }
}

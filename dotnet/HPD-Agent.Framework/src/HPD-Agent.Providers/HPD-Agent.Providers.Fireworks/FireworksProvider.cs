using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAICompatible;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Fireworks;

[HpdProvider("fireworks", "Fireworks AI", DocumentationUrl = "https://docs.fireworks.ai/")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "fireworks:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(FireworksProviderConfig), typeof(FireworksJsonContext))]
[HpdProviderSecretAlias("fireworks:ApiKey", "FIREWORKS_API_KEY")]
[HpdProviderSecretAlias("fireworks:Endpoint", "FIREWORKS_ENDPOINT", "FIREWORKS_BASE_URL")]
internal class FireworksProvider : IProvider, IProviderClientFactory<Meai.IChatClient>, IProviderSecretAliasProvider
{
    private static readonly Uri DefaultEndpoint = new("https://api.fireworks.ai/inference/v1/");
    private const string DefaultChatModel = "accounts/fireworks/models/llama-v3p1-8b-instruct";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        TopK = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        FrequencyPenalty = true,
        PresencePenalty = true,
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
        StreamingUsage = true
    };

    public string ProviderKey => "fireworks";
    public string DisplayName => "Fireworks AI";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("fireworks:ApiKey", new[] { "FIREWORKS_API_KEY" }),
            new("fireworks:Endpoint", new[] { "FIREWORKS_ENDPOINT", "FIREWORKS_BASE_URL" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    public ValueTask<ProviderClientConstruction<Meai.IChatClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException("For Fireworks AI, the ModelName must be configured.");
        }

        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var baseUri = config.Endpoint ?? DefaultEndpoint;

        var httpClient = context.Services.HttpClientFactory.CreateClient("hpd-provider-fireworks");
        httpClient.BaseAddress = baseUri;
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var client = new OpenAICompatibleChatClient(httpClient, new OpenAICompatibleChatClientOptions
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            ProviderUri = baseUri,
            DefaultModelId = config.ModelName,
            ChatCompletionsPath = "chat/completions",
            RequestProfile = ChatRequestProfile
        });

        Meai.IChatClient configured = new FireworksConfiguredChatClient(client, config.ModelName, baseUri);
        return ValueTask.FromResult(new ProviderClientConstruction<Meai.IChatClient>
        {
            Client = configured,
            Owner = ProviderClientConstructionUtilities.Own(httpClient, configured)
        });
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (config.Family != ProviderClientFamily.Chat)
        {
            errors.Add("Fireworks AI currently supports only the chat provider family.");
        }

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add("Model name is required for Fireworks AI");
        }


        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private sealed class FireworksConfiguredChatClient : Meai.IChatClient
    {
        private readonly Meai.IChatClient _innerClient;
        private readonly string _modelName;
        private readonly Uri _providerUri;
        private Meai.ChatClientMetadata? _metadata;

        public FireworksConfiguredChatClient(Meai.IChatClient innerClient, string modelName, Uri providerUri)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _providerUri = providerUri ?? throw new ArgumentNullException(nameof(providerUri));
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
            var merged = options?.Clone() ?? new Meai.ChatOptions();
            if (string.IsNullOrWhiteSpace(merged.ModelId))
                merged.ModelId = _modelName;

            return merged;
        }
    }
}

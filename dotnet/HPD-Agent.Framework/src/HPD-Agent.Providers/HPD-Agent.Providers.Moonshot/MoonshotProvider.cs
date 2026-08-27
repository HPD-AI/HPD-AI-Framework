using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAICompatible;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Moonshot;

[HpdProvider("moonshot", "Moonshot")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "moonshot:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(MoonshotProviderConfig), typeof(MoonshotJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(MoonshotChatRequestOptions), typeof(MoonshotJsonContext))]
[HpdProviderSecretAlias("moonshot:ApiKey", "MOONSHOT_API_KEY", "KIMI_API_KEY")]
[HpdProviderSecretAlias("moonshot:Endpoint", "MOONSHOT_ENDPOINT", "MOONSHOT_BASE_URL", "KIMI_ENDPOINT", "KIMI_BASE_URL")]
internal sealed class MoonshotProvider : IProvider, IProviderClientFactory<IChatClient>, IProviderSecretAliasProvider
{
    internal static readonly Uri DefaultEndpoint = new("https://api.moonshot.ai/v1/");
    internal const string DefaultChatModel = "kimi-k2.5";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxCompletionTokens,
        StopSequences = true,
        TextResponseFormat = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        Tools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        RequiredToolChoice = true,
        NamedToolChoice = true,
        StreamingUsage = true,
        Vision = true
    };

    public string ProviderKey => "moonshot";
    public string DisplayName => "Moonshot";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("moonshot:ApiKey", new[] { "MOONSHOT_API_KEY", "KIMI_API_KEY" }),
            new("moonshot:Endpoint", new[] { "MOONSHOT_ENDPOINT", "MOONSHOT_BASE_URL", "KIMI_ENDPOINT", "KIMI_BASE_URL" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    public ValueTask<ProviderClientConstruction<IChatClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var endpoint = config.Endpoint is null ? DefaultEndpoint : EnsureTrailingSlash(config.Endpoint);

        var modelName = string.IsNullOrWhiteSpace(config.ModelName)
            ? DefaultChatModel
            : config.ModelName;

        var httpClient = context.Services.HttpClientFactory.CreateClient("hpd-provider-moonshot");
        httpClient.BaseAddress = endpoint;
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        IChatClient client = new MoonshotChatClient(
            httpClient,
            new OpenAICompatibleChatClientOptions
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName,
                ProviderUri = endpoint,
                DefaultModelId = modelName,
                RequestProfile = ChatRequestProfile
            });
        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = client,
            Owner = ProviderClientConstructionUtilities.Own(client, httpClient)
        });
    }

    public IProviderErrorHandler CreateErrorHandler() => new MoonshotErrorHandler();

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://platform.moonshot.ai/docs/"),
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
                        ["SupportsThinking"] = true,
                        ["SupportsVision"] = true,
                        ["OpenAICompatibleEndpoint"] = "https://api.moonshot.ai/v1/"
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
            errors.Add("Moonshot currently supports only the chat provider family");
        }


        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(MoonshotProviderConfig config, List<string> errors)
    {
    }

    private static Uri EnsureTrailingSlash(Uri endpoint)
    {
        if (endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
            return endpoint;

        return new Uri(endpoint.AbsoluteUri + "/");
    }
}

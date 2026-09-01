using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Xai;

[HpdProvider("xai", "xAI")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "xai:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(XaiProviderConfig), typeof(XaiJsonContext))]
[HpdProviderSecretAlias("xai:ApiKey", "XAI_API_KEY")]
[HpdProviderSecretAlias("xai:Endpoint", "XAI_ENDPOINT", "XAI_BASE_URL")]
internal sealed class XaiProvider : IProvider, IProviderClientFactory<IChatClient>, IProviderSecretAliasProvider
{
    internal static readonly Uri DefaultEndpoint = new("https://api.x.ai/v1/");
    internal const string DefaultChatModel = "grok-4.3";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        TextResponseFormat = true,
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

    public string ProviderKey => "xai";
    public string DisplayName => "xAI";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("xai:ApiKey", new[] { "XAI_API_KEY" }),
            new("xai:Endpoint", new[] { "XAI_ENDPOINT", "XAI_BASE_URL" }),
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

        var httpClient = context.Services.HttpClientFactory.CreateClient("hpd-provider-xai");
        httpClient.BaseAddress = endpoint;
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        IChatClient client = new XaiChatClient(
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

    public IProviderErrorHandler CreateErrorHandler() => new XaiErrorHandler();

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://docs.x.ai/"),
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
                        ["SupportsReasoningEffort"] = true,
                        ["SupportsVision"] = true
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
            errors.Add("xAI currently supports only the chat provider family");
        }


        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static Uri EnsureTrailingSlash(Uri endpoint)
    {
        if (endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
            return endpoint;

        return new Uri(endpoint.AbsoluteUri + "/");
    }
}

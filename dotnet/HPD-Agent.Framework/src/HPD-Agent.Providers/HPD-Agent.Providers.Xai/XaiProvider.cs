using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Xai;

[HpdProvider("xai", "xAI")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(XaiProviderConfig), typeof(XaiJsonContext))]
[HpdProviderSecretAlias("xai:ApiKey", "XAI_API_KEY")]
[HpdProviderSecretAlias("xai:Endpoint", "XAI_ENDPOINT", "XAI_BASE_URL")]
internal sealed class XaiProvider : IChatClientProvider, IProviderSecretAliasProvider
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
    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var secrets = services?.GetService<ISecretResolver>();
        if (secrets is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var apiKey = await secrets.RequireAsync("xai:ApiKey", DisplayName, config.ApiKey, cancellationToken).ConfigureAwait(false);
        var endpointValue = await secrets.ResolveOrDefaultAsync("xai:Endpoint", config.Endpoint, cancellationToken).ConfigureAwait(false);
        var endpoint = string.IsNullOrWhiteSpace(endpointValue)
            ? DefaultEndpoint
            : EnsureTrailingSlash(new Uri(endpointValue, UriKind.Absolute));

        var modelName = string.IsNullOrWhiteSpace(config.ModelName)
            ? DefaultChatModel
            : config.ModelName;

        var httpClient = new HttpClient
        {
            BaseAddress = endpoint
        };
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        return new XaiChatClient(
            httpClient,
            new OpenAICompatibleChatClientOptions
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName,
                ProviderUri = endpoint,
                DefaultModelId = modelName,
                RequestProfile = ChatRequestProfile
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
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family != ProviderClientFamily.Chat)
        {
            errors.Add("xAI currently supports only the chat provider family");
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
            return endpoint;

        return new Uri(endpoint.AbsoluteUri + "/");
    }
}

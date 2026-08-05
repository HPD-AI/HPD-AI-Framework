using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Moonshot;

[HpdProvider("moonshot", "Moonshot")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(MoonshotProviderConfig), typeof(MoonshotJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(MoonshotChatRequestOptions), typeof(MoonshotJsonContext))]
[HpdProviderSecretAlias("moonshot:ApiKey", "MOONSHOT_API_KEY", "KIMI_API_KEY")]
[HpdProviderSecretAlias("moonshot:Endpoint", "MOONSHOT_ENDPOINT", "MOONSHOT_BASE_URL", "KIMI_ENDPOINT", "KIMI_BASE_URL")]
internal sealed class MoonshotProvider : IChatClientProvider
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

        var apiKey = await secrets.RequireAsync("moonshot:ApiKey", DisplayName, config.ApiKey, cancellationToken).ConfigureAwait(false);
        var endpointValue = await secrets.ResolveOrDefaultAsync("moonshot:Endpoint", config.Endpoint, cancellationToken).ConfigureAwait(false);
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

        return new MoonshotChatClient(
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
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family != ProviderClientFamily.Chat)
        {
            errors.Add("Moonshot currently supports only the chat provider family");
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

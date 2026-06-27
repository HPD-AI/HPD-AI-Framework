using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Xai;

internal sealed class XaiProvider : IChatClientProvider
{
    internal static readonly Uri DefaultEndpoint = new("https://api.x.ai/v1/");
    internal const string DefaultChatModel = "grok-4.3";

    public string ProviderKey => "xai";
    public string DisplayName => "xAI";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in XaiProviderModule.")]
    public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var secrets = services?.GetService<ISecretResolver>();
        if (secrets is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var apiKeyTask = secrets.RequireAsync("xai:ApiKey", DisplayName, config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();

        var endpointTask = secrets.ResolveOrDefaultAsync("xai:Endpoint", config.Endpoint, CancellationToken.None);
        var endpointValue = endpointTask.GetAwaiter().GetResult();
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
                DefaultModelId = modelName
            },
            config.GetProviderConfig<XaiProviderConfig>());
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
                        ["SupportsVision"] = false
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in XaiProviderModule.")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
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

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            errors.Add("API key is required for xAI. " +
                       "Set it via the apiKey parameter, XAI_API_KEY environment variable, or configuration.");
        }

        var xaiConfig = config.GetProviderConfig<XaiProviderConfig>();
        if (xaiConfig is not null)
        {
            ValidateProviderOptions(xaiConfig, errors);
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(XaiProviderConfig config, List<string> errors)
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

        if (config.ReasoningEffort is { Length: > 0 } reasoningEffort &&
            !string.Equals(reasoningEffort, "low", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(reasoningEffort, "medium", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(reasoningEffort, "high", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ReasoningEffort must be one of: low, medium, high");
        }
    }

    private static Uri EnsureTrailingSlash(Uri endpoint)
    {
        if (endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
            return endpoint;

        return new Uri(endpoint.AbsoluteUri + "/");
    }
}

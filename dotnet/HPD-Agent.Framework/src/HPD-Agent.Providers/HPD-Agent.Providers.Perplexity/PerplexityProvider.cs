using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Perplexity;

internal sealed class PerplexityProvider : OpenAICompatibleChatProviderBase<PerplexityProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.perplexity.ai/");
    internal const string DefaultChatModel = "sonar-pro";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "perplexity",
        DisplayName = "Perplexity",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "perplexity:ApiKey",
        EndpointSecretKey = "perplexity:Endpoint",
        ProviderUri = new Uri("https://www.perplexity.ai/"),
        DocumentationUri = new Uri("https://docs.perplexity.ai/docs/sonar/quickstart"),
        RequiresApiKey = true,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.perplexity.ai/",
            ["SupportsCitations"] = true,
            ["SupportsSearchGrounding"] = true
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

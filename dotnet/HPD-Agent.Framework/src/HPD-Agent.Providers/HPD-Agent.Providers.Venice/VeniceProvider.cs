using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Venice;

internal sealed class VeniceProvider : OpenAICompatibleChatProviderBase<VeniceProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.venice.ai/api/v1/");
    internal const string DefaultChatModel = "venice-uncensored";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "venice",
        DisplayName = "Venice.ai",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "venice:ApiKey",
        EndpointSecretKey = "venice:Endpoint",
        ProviderUri = new Uri("https://venice.ai/"),
        DocumentationUri = new Uri("https://docs.venice.ai/api-reference/api-spec"),
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.venice.ai/api/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Nebius;

internal sealed class NebiusProvider : OpenAICompatibleChatProviderBase<NebiusProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.tokenfactory.nebius.com/v1/");
    internal const string DefaultChatModel = "meta-llama/Meta-Llama-3.1-70B-Instruct";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "nebius",
        DisplayName = "Nebius Token Factory",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "nebius:ApiKey",
        EndpointSecretKey = "nebius:Endpoint",
        ProviderUri = new Uri("https://nebius.com/services/token-factory"),
        DocumentationUri = new Uri("https://docs.tokenfactory.nebius.com/api-reference/introduction"),
        RequiresApiKey = true,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.tokenfactory.nebius.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

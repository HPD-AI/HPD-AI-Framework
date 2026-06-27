using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Scaleway;

internal sealed class ScalewayProvider : OpenAICompatibleChatProviderBase<ScalewayProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.scaleway.ai/v1/");
    internal const string DefaultChatModel = "qwen3.5-397b-a17b";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "scaleway",
        DisplayName = "Scaleway Generative APIs",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "scaleway:ApiKey",
        EndpointSecretKey = "scaleway:Endpoint",
        ProviderUri = new Uri("https://www.scaleway.com/en/generative-apis/"),
        DocumentationUri = new Uri("https://www.scaleway.com/en/docs/generative-apis/reference-content/openai-compatibility/"),
        RequiresApiKey = true,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.scaleway.ai/v1/",
            ["SupportsDedicatedEndpoints"] = true
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

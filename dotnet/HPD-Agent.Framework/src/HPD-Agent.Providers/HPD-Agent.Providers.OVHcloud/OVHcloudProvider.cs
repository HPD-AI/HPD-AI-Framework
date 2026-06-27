using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.OVHcloud;

internal sealed class OVHcloudProvider : OpenAICompatibleChatProviderBase<OVHcloudProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://oai.endpoints.kepler.ai.cloud.ovh.net/v1/");
    internal const string DefaultChatModel = "gpt-oss-120b";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "ovhcloud",
        DisplayName = "OVHcloud AI Endpoints",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "ovhcloud:ApiKey",
        EndpointSecretKey = "ovhcloud:Endpoint",
        ProviderUri = new Uri("https://www.ovhcloud.com/"),
        DocumentationUri = new Uri("https://docs.ovhcloud.com/en/guides/public-cloud/ai-machine-learning/ai-endpoints-getting-started/"),
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["OpenAICompatibleEndpoint"] = "https://oai.endpoints.kepler.ai.cloud.ovh.net/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

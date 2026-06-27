using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Zai;

internal sealed class ZaiProvider : OpenAICompatibleChatProviderBase<ZaiProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.z.ai/api/paas/v4/");
    internal const string DefaultChatModel = "glm-4.7";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "zai",
        DisplayName = "Z.AI",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "zai:ApiKey",
        EndpointSecretKey = "zai:Endpoint",
        ProviderUri = new Uri("https://z.ai/"),
        DocumentationUri = new Uri("https://docs.z.ai/guides/develop/openai/python"),
        RequiresApiKey = true,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.z.ai/api/paas/v4/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

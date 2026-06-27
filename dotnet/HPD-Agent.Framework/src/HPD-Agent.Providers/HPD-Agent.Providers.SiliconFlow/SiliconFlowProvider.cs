using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.SiliconFlow;

internal sealed class SiliconFlowProvider : OpenAICompatibleChatProviderBase<SiliconFlowProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.siliconflow.com/v1/");
    internal const string DefaultChatModel = "Qwen/Qwen3-32B";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "siliconflow",
        DisplayName = "SiliconFlow",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "siliconflow:ApiKey",
        EndpointSecretKey = "siliconflow:Endpoint",
        ProviderUri = new Uri("https://www.siliconflow.com/"),
        DocumentationUri = new Uri("https://docs.siliconflow.com/en/api-reference/chat-completions/chat-completions"),
        RequiresApiKey = true,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.siliconflow.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

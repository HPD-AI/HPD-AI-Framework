using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.MiniMax;

internal sealed class MiniMaxProvider : OpenAICompatibleChatProviderBase<MiniMaxProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.minimax.io/v1/");
    internal const string DefaultChatModel = "MiniMax-M3";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "minimax",
        DisplayName = "MiniMax",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "minimax:ApiKey",
        EndpointSecretKey = "minimax:Endpoint",
        ProviderUri = new Uri("https://www.minimax.io/"),
        DocumentationUri = new Uri("https://platform.minimax.io/docs/api-reference/text-chat-openai"),
        RequiresApiKey = true,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.minimax.io/v1/",
            ["SupportsVisionInput"] = true,
            ["SupportsVideoInput"] = true
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

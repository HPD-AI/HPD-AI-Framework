using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.MiniMax;

[HpdProvider("minimax", "MiniMax")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(MiniMaxProviderConfig), typeof(MiniMaxJsonContext))]
[HpdProviderSecretAlias("minimax:ApiKey", "MINIMAX_API_KEY")]
[HpdProviderSecretAlias("minimax:Endpoint", "MINIMAX_ENDPOINT", "MINIMAX_BASE_URL", "MINIMAX_API_BASE")]
internal sealed class MiniMaxProvider : OpenAICompatibleChatProviderBase<MiniMaxProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.minimax.io/v1/");
    internal const string DefaultChatModel = "MiniMax-M3";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxCompletionTokens,
        Tools = true,
        StreamingUsage = true,
        Vision = true,
        ApplyReasoning = static (request, reasoning) =>
        {
            request.Thinking = reasoning.Effort switch
            {
                Microsoft.Extensions.AI.ReasoningEffort.None =>
                    new OpenAICompatibleThinkingRequest { Type = "disabled" },
                Microsoft.Extensions.AI.ReasoningEffort.Low or
                Microsoft.Extensions.AI.ReasoningEffort.Medium or
                Microsoft.Extensions.AI.ReasoningEffort.High or
                Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh =>
                    new OpenAICompatibleThinkingRequest { Type = "adaptive" },
                _ => null
            };
        }
    };

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
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsReasoning"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.minimax.io/v1/",
            ["SupportsVisionInput"] = true,
            ["SupportsVideoInput"] = true
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

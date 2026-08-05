using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Zai;

[HpdProvider("zai", "Z.AI")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(ZaiProviderConfig), typeof(ZaiJsonContext))]
[HpdProviderSecretAlias("zai:ApiKey", "ZAI_API_KEY", "Z_AI_API_KEY", "BIGMODEL_API_KEY")]
[HpdProviderSecretAlias("zai:Endpoint", "ZAI_ENDPOINT", "ZAI_BASE_URL", "Z_AI_ENDPOINT", "Z_AI_BASE_URL", "BIGMODEL_ENDPOINT", "BIGMODEL_BASE_URL")]
internal sealed class ZaiProvider : OpenAICompatibleChatProviderBase<ZaiProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.z.ai/api/paas/v4/");
    internal const string DefaultChatModel = "glm-4.7";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        StopSequences = true,
        TextResponseFormat = true,
        JsonObjectResponseFormat = true,
        Tools = true,
        AutoToolChoice = true,
        ApplyReasoning = ApplyReasoning
    };

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
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsReasoning"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.z.ai/api/paas/v4/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);

    /// <summary>
    /// Translates reasoning configuration for GLM models that use Z.AI's thinking object.
    /// </summary>
    private static void ApplyReasoning(
        OpenAICompatibleChatRequest request,
        Microsoft.Extensions.AI.ReasoningOptions reasoning)
    {
        if (reasoning.Effort is null)
        {
            return;
        }

        request.Thinking = new OpenAICompatibleThinkingRequest
        {
            Type = reasoning.Effort == Microsoft.Extensions.AI.ReasoningEffort.None ? "disabled" : "enabled"
        };
    }
}

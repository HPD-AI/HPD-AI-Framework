using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.SiliconFlow;

[HpdProvider("siliconflow", "SiliconFlow")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(SiliconFlowProviderConfig), typeof(SiliconFlowJsonContext))]
[HpdProviderSecretAlias("siliconflow:ApiKey", "SILICONFLOW_API_KEY")]
[HpdProviderSecretAlias("siliconflow:Endpoint", "SILICONFLOW_ENDPOINT", "SILICONFLOW_BASE_URL")]
internal sealed class SiliconFlowProvider : OpenAICompatibleChatProviderBase<SiliconFlowProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.siliconflow.com/v1/");
    internal const string DefaultChatModel = "Qwen/Qwen3-32B";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        TopK = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        FrequencyPenalty = true,
        StopSequences = true,
        Tools = true
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "siliconflow",
        DisplayName = "SiliconFlow",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "siliconflow:ApiKey",
        EndpointSecretKey = "siliconflow:Endpoint",
        ApiKeyEnvironmentVariables = new[] { "SILICONFLOW_API_KEY" },
        EndpointEnvironmentVariables = new[] { "SILICONFLOW_ENDPOINT", "SILICONFLOW_BASE_URL" },
        ProviderUri = new Uri("https://www.siliconflow.com/"),
        DocumentationUri = new Uri("https://docs.siliconflow.com/en/api-reference/chat-completions/chat-completions"),
        RequiresApiKey = true,
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.siliconflow.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

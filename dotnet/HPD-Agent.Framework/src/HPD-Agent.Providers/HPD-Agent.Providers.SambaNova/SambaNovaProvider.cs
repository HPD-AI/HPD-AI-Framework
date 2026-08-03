using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.SambaNova;

[HpdProvider("sambanova", "SambaNova")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(SambaNovaProviderConfig), typeof(SambaNovaJsonContext))]
[HpdProviderSecretAlias("sambanova:ApiKey", "SAMBANOVA_API_KEY")]
[HpdProviderSecretAlias("sambanova:Endpoint", "SAMBANOVA_ENDPOINT", "SAMBANOVA_BASE_URL")]
internal sealed class SambaNovaProvider : OpenAICompatibleChatProviderBase<SambaNovaProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.sambanova.ai/v1/");
    internal const string DefaultChatModel = "Meta-Llama-3.3-70B-Instruct";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        TopK = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        StopSequences = true,
        Seed = true,
        TextResponseFormat = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        Tools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        RequiredToolChoice = true,
        ParallelToolCalls = true,
        StreamingUsage = true,
        ApplyReasoning = ApplyReasoning
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "sambanova",
        DisplayName = "SambaNova",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "sambanova:ApiKey",
        EndpointSecretKey = "sambanova:Endpoint",
        ProviderUri = new Uri("https://sambanova.ai/"),
        DocumentationUri = new Uri("https://docs.sambanova.ai/docs/en/features/openai-compatibility"),
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["SupportsReasoning"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.sambanova.ai/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);

    /// <summary>
    /// Maps MEAI reasoning levels to the reasoning efforts accepted by SambaNova.
    /// </summary>
    private static void ApplyReasoning(
        OpenAICompatibleChatRequest request,
        Microsoft.Extensions.AI.ReasoningOptions reasoning)
    {
        request.ReasoningEffort = reasoning.Effort switch
        {
            Microsoft.Extensions.AI.ReasoningEffort.Low => "low",
            Microsoft.Extensions.AI.ReasoningEffort.Medium => "medium",
            Microsoft.Extensions.AI.ReasoningEffort.High => "high",
            _ => null
        };
    }
}

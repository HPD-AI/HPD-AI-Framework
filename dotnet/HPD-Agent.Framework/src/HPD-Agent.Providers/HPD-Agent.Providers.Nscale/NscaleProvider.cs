using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Nscale;

[HpdProvider("nscale", "Nscale")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(NscaleProviderConfig), typeof(NscaleJsonContext))]
[HpdProviderSecretAlias("nscale:ApiKey", "NSCALE_API_KEY")]
[HpdProviderSecretAlias("nscale:Endpoint", "NSCALE_ENDPOINT", "NSCALE_BASE_URL")]
internal sealed class NscaleProvider : OpenAICompatibleChatProviderBase<NscaleProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://inference.api.nscale.com/v1/");
    internal const string DefaultChatModel = "Qwen/Qwen3-Coder-480B-A35B-Instruct-FP8";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        FrequencyPenalty = true,
        PresencePenalty = true,
        StopSequences = true,
        Seed = true,
        Tools = true,
        AutoToolChoice = true,
        StreamingUsage = true,
        ApplyReasoning = ApplyReasoning
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "nscale",
        DisplayName = "Nscale",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "nscale:ApiKey",
        EndpointSecretKey = "nscale:Endpoint",
        ApiKeyEnvironmentVariables = new[] { "NSCALE_API_KEY" },
        EndpointEnvironmentVariables = new[] { "NSCALE_ENDPOINT", "NSCALE_BASE_URL" },
        ProviderUri = new Uri("https://www.nscale.com/"),
        DocumentationUri = new Uri("https://www.nscale.com/"),
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsSeed"] = true,
            ["SupportsReasoning"] = true,
            ["OpenAICompatibleEndpoint"] = "https://inference.api.nscale.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);

    /// <summary>
    /// Applies the reasoning effort explicitly documented by Nscale.
    /// </summary>
    private static void ApplyReasoning(
        OpenAICompatibleChatRequest request,
        Microsoft.Extensions.AI.ReasoningOptions reasoning)
    {
        if (reasoning.Effort == Microsoft.Extensions.AI.ReasoningEffort.Medium)
        {
            request.ReasoningEffort = "medium";
        }
    }
}

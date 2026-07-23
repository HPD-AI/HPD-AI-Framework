using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.DeepSeek;

internal sealed class DeepSeekProvider : OpenAICompatibleChatProviderBase<DeepSeekProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.deepseek.com/v1/");
    internal const string DefaultChatModel = "deepseek-v4-flash";

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
        NoneToolChoice = true,
        RequiredToolChoice = true,
        NamedToolChoice = true,
        StreamingUsage = true,
        ApplyReasoning = ApplyReasoning
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "deepseek",
        DisplayName = "DeepSeek",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "deepseek:ApiKey",
        EndpointSecretKey = "deepseek:Endpoint",
        ProviderUri = new Uri("https://deepseek.com/"),
        DocumentationUri = new Uri("https://api-docs.deepseek.com/"),
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsReasoning"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.deepseek.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);

    /// <summary>
    /// Applies DeepSeek's thinking and reasoning-effort request fields.
    /// </summary>
    private static void ApplyReasoning(
        OpenAICompatibleChatRequest request,
        Microsoft.Extensions.AI.ReasoningOptions reasoning)
    {
        switch (reasoning.Effort)
        {
            case Microsoft.Extensions.AI.ReasoningEffort.None:
                request.Thinking = new OpenAICompatibleThinkingRequest { Type = "disabled" };
                break;
            case Microsoft.Extensions.AI.ReasoningEffort.Low:
            case Microsoft.Extensions.AI.ReasoningEffort.Medium:
            case Microsoft.Extensions.AI.ReasoningEffort.High:
                request.Thinking = new OpenAICompatibleThinkingRequest { Type = "enabled" };
                request.ReasoningEffort = "high";
                break;
            case Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh:
                request.Thinking = new OpenAICompatibleThinkingRequest { Type = "enabled" };
                request.ReasoningEffort = "max";
                break;
        }
    }
}

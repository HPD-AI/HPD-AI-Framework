using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Perplexity;

internal sealed class PerplexityProvider : OpenAICompatibleChatProviderBase<PerplexityProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.perplexity.ai/");
    internal const string DefaultChatModel = "sonar-pro";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        StopSequences = true,
        TextResponseFormat = true,
        JsonSchemaResponseFormat = true,
        Vision = true,
        ApplyReasoning = ApplyReasoning
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "perplexity",
        DisplayName = "Perplexity",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "perplexity:ApiKey",
        EndpointSecretKey = "perplexity:Endpoint",
        ProviderUri = new Uri("https://www.perplexity.ai/"),
        DocumentationUri = new Uri("https://docs.perplexity.ai/docs/sonar/quickstart"),
        RequiresApiKey = true,
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsReasoning"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.perplexity.ai/",
            ["SupportsCitations"] = true,
            ["SupportsSearchGrounding"] = true
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);

    /// <summary>
    /// Maps representable MEAI reasoning efforts to Perplexity Sonar values.
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

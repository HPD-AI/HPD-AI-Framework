using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Venice;

[HpdProvider("venice", "Venice.ai")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "venice:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(VeniceProviderConfig), typeof(VeniceJsonContext))]
[HpdProviderSecretAlias("venice:ApiKey", "VENICE_API_KEY")]
[HpdProviderSecretAlias("venice:Endpoint", "VENICE_ENDPOINT", "VENICE_BASE_URL")]
internal sealed class VeniceProvider : OpenAICompatibleChatProviderBase<VeniceProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.venice.ai/api/v1/");
    internal const string DefaultChatModel = "venice-uncensored";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        TopK = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        FrequencyPenalty = true,
        PresencePenalty = true,
        StopSequences = true,
        Seed = true,
        StreamingUsage = true,
        TextResponseFormat = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        StrictJsonSchema = true,
        Tools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        RequiredToolChoice = true,
        NamedToolChoice = true,
        ParallelToolCalls = true,
        ApplyReasoning = ApplyReasoning
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "venice",
        DisplayName = "Venice.ai",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "venice:ApiKey",
        EndpointSecretKey = "venice:Endpoint",
        ApiKeyEnvironmentVariables = new[] { "VENICE_API_KEY" },
        EndpointEnvironmentVariables = new[] { "VENICE_ENDPOINT", "VENICE_BASE_URL" },
        ProviderUri = new Uri("https://venice.ai/"),
        DocumentationUri = new Uri("https://docs.venice.ai/api-reference/api-spec"),
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["SupportsReasoning"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.venice.ai/api/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;
    protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<VeniceProviderConfig> ConfigurationTypeInfo => VeniceJsonContext.Default.VeniceProviderConfig;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);

    /// <summary>
    /// Maps MEAI reasoning levels to Venice's portable reasoning-effort values.
    /// </summary>
    private static void ApplyReasoning(
        OpenAICompatibleChatRequest request,
        Microsoft.Extensions.AI.ReasoningOptions reasoning)
    {
        request.ReasoningEffort = reasoning.Effort switch
        {
            Microsoft.Extensions.AI.ReasoningEffort.None => "none",
            Microsoft.Extensions.AI.ReasoningEffort.Low => "low",
            Microsoft.Extensions.AI.ReasoningEffort.Medium => "medium",
            Microsoft.Extensions.AI.ReasoningEffort.High => "high",
            Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh => "xhigh",
            _ => null
        };
    }
}

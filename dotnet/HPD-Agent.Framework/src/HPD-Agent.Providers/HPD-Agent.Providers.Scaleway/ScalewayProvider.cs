using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Scaleway;

[HpdProvider("scaleway", "Scaleway Generative APIs")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(ScalewayProviderConfig), typeof(ScalewayJsonContext))]
[HpdProviderSecretAlias("scaleway:ApiKey", "SCW_SECRET_KEY", "SCALEWAY_API_KEY", "SCW_API_KEY")]
[HpdProviderSecretAlias("scaleway:Endpoint", "SCALEWAY_ENDPOINT", "SCALEWAY_BASE_URL", "SCW_ENDPOINT", "SCW_BASE_URL")]
internal sealed class ScalewayProvider : OpenAICompatibleChatProviderBase<ScalewayProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.scaleway.ai/v1/");
    internal const string DefaultChatModel = "qwen3.5-397b-a17b";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        PresencePenalty = true,
        StopSequences = true,
        Seed = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        Tools = true,
        AutoToolChoice = true,
        NamedToolChoice = true
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "scaleway",
        DisplayName = "Scaleway Generative APIs",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "scaleway:ApiKey",
        EndpointSecretKey = "scaleway:Endpoint",
        ApiKeyEnvironmentVariables = new[] { "SCW_SECRET_KEY", "SCALEWAY_API_KEY", "SCW_API_KEY" },
        EndpointEnvironmentVariables = new[] { "SCALEWAY_ENDPOINT", "SCALEWAY_BASE_URL", "SCW_ENDPOINT", "SCW_BASE_URL" },
        ProviderUri = new Uri("https://www.scaleway.com/en/generative-apis/"),
        DocumentationUri = new Uri("https://www.scaleway.com/en/docs/generative-apis/reference-content/openai-compatibility/"),
        RequiresApiKey = true,
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.scaleway.ai/v1/",
            ["SupportsDedicatedEndpoints"] = true
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

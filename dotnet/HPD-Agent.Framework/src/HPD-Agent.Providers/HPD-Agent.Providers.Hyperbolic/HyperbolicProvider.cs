using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Hyperbolic;

[HpdProvider("hyperbolic", "Hyperbolic")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(HyperbolicProviderConfig), typeof(HyperbolicJsonContext))]
[HpdProviderSecretAlias("hyperbolic:ApiKey", "HYPERBOLIC_API_KEY")]
[HpdProviderSecretAlias("hyperbolic:Endpoint", "HYPERBOLIC_ENDPOINT", "HYPERBOLIC_BASE_URL")]
internal sealed class HyperbolicProvider : OpenAICompatibleChatProviderBase<HyperbolicProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.hyperbolic.xyz/v1/");
    internal const string DefaultChatModel = "Qwen/Qwen2.5-72B-Instruct";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        StopSequences = true
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "hyperbolic",
        DisplayName = "Hyperbolic",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "hyperbolic:ApiKey",
        EndpointSecretKey = "hyperbolic:Endpoint",
        ProviderUri = new Uri("https://hyperbolic.xyz/"),
        DocumentationUri = new Uri("https://docs.hyperbolic.xyz/"),
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.hyperbolic.xyz/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

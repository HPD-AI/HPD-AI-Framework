using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Nebius;

[HpdProvider("nebius", "Nebius Token Factory")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "nebius:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(NebiusProviderConfig), typeof(NebiusJsonContext))]
[HpdProviderSecretAlias("nebius:ApiKey", "NEBIUS_API_KEY")]
[HpdProviderSecretAlias("nebius:Endpoint", "NEBIUS_ENDPOINT", "NEBIUS_BASE_URL")]
internal sealed class NebiusProvider : OpenAICompatibleChatProviderBase<NebiusProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.tokenfactory.nebius.com/v1/");
    internal const string DefaultChatModel = "meta-llama/Meta-Llama-3.1-70B-Instruct";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        FrequencyPenalty = true,
        PresencePenalty = true,
        StopSequences = true,
        TextResponseFormat = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        Tools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        RequiredToolChoice = true,
        NamedToolChoice = true
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "nebius",
        DisplayName = "Nebius Token Factory",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "nebius:ApiKey",
        EndpointSecretKey = "nebius:Endpoint",
        ApiKeyEnvironmentVariables = new[] { "NEBIUS_API_KEY" },
        EndpointEnvironmentVariables = new[] { "NEBIUS_ENDPOINT", "NEBIUS_BASE_URL" },
        ProviderUri = new Uri("https://nebius.com/services/token-factory"),
        DocumentationUri = new Uri("https://docs.tokenfactory.nebius.com/api-reference/introduction"),
        RequiresApiKey = true,
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.tokenfactory.nebius.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;
    protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<NebiusProviderConfig> ConfigurationTypeInfo => NebiusJsonContext.Default.NebiusProviderConfig;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

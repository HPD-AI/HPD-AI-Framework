using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Cerebras;

[HpdProvider("cerebras", "Cerebras", DocumentationUrl = "https://inference-docs.cerebras.ai/resources/openai")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "cerebras:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(CerebrasProviderConfig), typeof(CerebrasJsonContext))]
[HpdProviderSecretAlias("cerebras:ApiKey", "CEREBRAS_API_KEY")]
[HpdProviderSecretAlias("cerebras:Endpoint", "CEREBRAS_ENDPOINT", "CEREBRAS_BASE_URL")]
internal sealed class CerebrasProvider : OpenAICompatibleChatProviderBase<CerebrasProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.cerebras.ai/v1/");
    internal const string DefaultChatModel = "gpt-oss-120b";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxCompletionTokens,
        FrequencyPenalty = true,
        PresencePenalty = true,
        StopSequences = true,
        Seed = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        Tools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        RequiredToolChoice = true,
        NamedToolChoice = true,
        ParallelToolCalls = true
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "cerebras",
        DisplayName = "Cerebras",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "cerebras:ApiKey",
        EndpointSecretKey = "cerebras:Endpoint",
        ApiKeyEnvironmentVariables = new[] { "CEREBRAS_API_KEY" },
        EndpointEnvironmentVariables = new[] { "CEREBRAS_ENDPOINT", "CEREBRAS_BASE_URL" },
        ProviderUri = new Uri("https://cerebras.ai/"),
        DocumentationUri = new Uri("https://inference-docs.cerebras.ai/resources/openai"),
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.cerebras.ai/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;
    protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<CerebrasProviderConfig> ConfigurationTypeInfo => CerebrasJsonContext.Default.CerebrasProviderConfig;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.LMStudio;

[HpdProvider("lmstudio", "LM Studio")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(LMStudioProviderConfig), typeof(LMStudioJsonContext))]
[HpdProviderSecretAlias("lmstudio:ApiKey", "LMSTUDIO_API_KEY", "LM_STUDIO_API_KEY")]
[HpdProviderSecretAlias("lmstudio:Endpoint", "LMSTUDIO_ENDPOINT", "LMSTUDIO_BASE_URL", "LMSTUDIO_API_BASE", "LM_STUDIO_ENDPOINT", "LM_STUDIO_BASE_URL", "LM_STUDIO_API_BASE")]
internal sealed class LMStudioProvider : OpenAICompatibleChatProviderBase<LMStudioProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("http://localhost:1234/v1/");
    internal const string DefaultChatModel = "local-model";

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
        JsonSchemaResponseFormat = true,
        Tools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        RequiredToolChoice = true,
        Vision = true
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "lmstudio",
        DisplayName = "LM Studio",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "lmstudio:ApiKey",
        EndpointSecretKey = "lmstudio:Endpoint",
        ProviderUri = new Uri("https://lmstudio.ai/"),
        DocumentationUri = new Uri("https://lmstudio.ai/docs/developer/openai-compat"),
        RequiresApiKey = false,
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["SupportsVisionInput"] = true,
            ["OpenAICompatibleEndpoint"] = "http://localhost:1234/v1/",
            ["SupportsLocalRuntime"] = true
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

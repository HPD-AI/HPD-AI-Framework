using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.LMStudio;

internal sealed class LMStudioProvider : OpenAICompatibleChatProviderBase<LMStudioProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("http://localhost:1234/v1/");
    internal const string DefaultChatModel = "local-model";

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
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "http://localhost:1234/v1/",
            ["SupportsLocalRuntime"] = true
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

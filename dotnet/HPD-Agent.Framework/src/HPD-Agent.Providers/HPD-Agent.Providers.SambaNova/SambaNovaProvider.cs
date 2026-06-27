using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.SambaNova;

internal sealed class SambaNovaProvider : OpenAICompatibleChatProviderBase<SambaNovaProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.sambanova.ai/v1/");
    internal const string DefaultChatModel = "Meta-Llama-3.3-70B-Instruct";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "sambanova",
        DisplayName = "SambaNova",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "sambanova:ApiKey",
        EndpointSecretKey = "sambanova:Endpoint",
        ProviderUri = new Uri("https://sambanova.ai/"),
        DocumentationUri = new Uri("https://docs.sambanova.ai/docs/en/features/openai-compatibility"),
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.sambanova.ai/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

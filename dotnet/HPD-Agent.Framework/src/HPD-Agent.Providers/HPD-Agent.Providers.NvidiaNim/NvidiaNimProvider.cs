using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.NvidiaNim;

internal sealed class NvidiaNimProvider : OpenAICompatibleChatProviderBase<NvidiaNimProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://integrate.api.nvidia.com/v1/");
    internal const string DefaultChatModel = "meta/llama-3.1-70b-instruct";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "nvidia-nim",
        DisplayName = "NVIDIA NIM",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "nvidia-nim:ApiKey",
        EndpointSecretKey = "nvidia-nim:Endpoint",
        ProviderUri = new Uri("https://build.nvidia.com/"),
        DocumentationUri = new Uri("https://docs.api.nvidia.com/nim/reference/google-codegemma-7b-infer"),
        RequiresApiKey = true,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://integrate.api.nvidia.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

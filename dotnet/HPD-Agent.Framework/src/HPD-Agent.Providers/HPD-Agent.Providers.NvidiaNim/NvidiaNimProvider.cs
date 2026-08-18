using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.NvidiaNim;

[HpdProvider("nvidia-nim", "NVIDIA NIM")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(NvidiaNimProviderConfig), typeof(NvidiaNimJsonContext))]
[HpdProviderSecretAlias("nvidia-nim:ApiKey", "NVIDIA_API_KEY", "NVIDIA_NIM_API_KEY")]
[HpdProviderSecretAlias("nvidia-nim:Endpoint", "NVIDIA_NIM_ENDPOINT", "NVIDIA_NIM_BASE_URL", "NVIDIA_ENDPOINT", "NVIDIA_BASE_URL")]
internal sealed class NvidiaNimProvider : OpenAICompatibleChatProviderBase<NvidiaNimProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://integrate.api.nvidia.com/v1/");
    internal const string DefaultChatModel = "meta/llama-3.1-70b-instruct";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
        Temperature = true,
        TopP = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens
    };

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "nvidia-nim",
        DisplayName = "NVIDIA NIM",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "nvidia-nim:ApiKey",
        EndpointSecretKey = "nvidia-nim:Endpoint",
        ApiKeyEnvironmentVariables = new[] { "NVIDIA_API_KEY", "NVIDIA_NIM_API_KEY" },
        EndpointEnvironmentVariables = new[] { "NVIDIA_NIM_ENDPOINT", "NVIDIA_NIM_BASE_URL", "NVIDIA_ENDPOINT", "NVIDIA_BASE_URL" },
        ProviderUri = new Uri("https://build.nvidia.com/"),
        DocumentationUri = new Uri("https://docs.api.nvidia.com/nim/reference/google-codegemma-7b-infer"),
        RequiresApiKey = true,
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["OpenAICompatibleEndpoint"] = "https://integrate.api.nvidia.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

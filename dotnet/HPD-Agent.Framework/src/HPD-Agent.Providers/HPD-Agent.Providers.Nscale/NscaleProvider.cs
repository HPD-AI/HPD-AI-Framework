using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Nscale;

internal sealed class NscaleProvider : OpenAICompatibleChatProviderBase<NscaleProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://inference.api.nscale.com/v1/");
    internal const string DefaultChatModel = "Qwen/Qwen3-Coder-480B-A35B-Instruct-FP8";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "nscale",
        DisplayName = "Nscale",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "nscale:ApiKey",
        EndpointSecretKey = "nscale:Endpoint",
        ProviderUri = new Uri("https://www.nscale.com/"),
        DocumentationUri = new Uri("https://www.nscale.com/"),
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["OpenAICompatibleEndpoint"] = "https://inference.api.nscale.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

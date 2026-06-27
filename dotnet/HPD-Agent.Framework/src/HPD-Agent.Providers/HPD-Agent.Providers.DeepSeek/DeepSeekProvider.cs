using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.DeepSeek;

internal sealed class DeepSeekProvider : OpenAICompatibleChatProviderBase<DeepSeekProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://api.deepseek.com/v1/");
    internal const string DefaultChatModel = "deepseek-v4-flash";

    private static readonly OpenAICompatibleProviderDefinition ProviderDefinition = new()
    {
        ProviderKey = "deepseek",
        DisplayName = "DeepSeek",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "deepseek:ApiKey",
        EndpointSecretKey = "deepseek:Endpoint",
        ProviderUri = new Uri("https://deepseek.com/"),
        DocumentationUri = new Uri("https://api-docs.deepseek.com/"),
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["SupportsSeed"] = true,
            ["OpenAICompatibleEndpoint"] = "https://api.deepseek.com/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

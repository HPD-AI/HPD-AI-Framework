using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.OVHcloud;

[HpdProvider("ovhcloud", "OVHcloud AI Endpoints")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "ovhcloud:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(OVHcloudProviderConfig), typeof(OVHcloudJsonContext))]
[HpdProviderSecretAlias("ovhcloud:ApiKey", "OVHCLOUD_API_KEY")]
[HpdProviderSecretAlias("ovhcloud:Endpoint", "OVHCLOUD_ENDPOINT", "OVHCLOUD_BASE_URL")]
internal sealed class OVHcloudProvider : OpenAICompatibleChatProviderBase<OVHcloudProviderConfig>
{
    internal static readonly Uri DefaultEndpoint = new("https://oai.endpoints.kepler.ai.cloud.ovh.net/v1/");
    internal const string DefaultChatModel = "gpt-oss-120b";

    internal static readonly OpenAICompatibleRequestProfile ChatRequestProfile = new()
    {
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
        ProviderKey = "ovhcloud",
        DisplayName = "OVHcloud AI Endpoints",
        DefaultEndpoint = DefaultEndpoint,
        DefaultModelId = DefaultChatModel,
        ApiKeySecretKey = "ovhcloud:ApiKey",
        EndpointSecretKey = "ovhcloud:Endpoint",
        ApiKeyEnvironmentVariables = new[] { "OVHCLOUD_API_KEY" },
        EndpointEnvironmentVariables = new[] { "OVHCLOUD_ENDPOINT", "OVHCLOUD_BASE_URL" },
        ProviderUri = new Uri("https://www.ovhcloud.com/"),
        DocumentationUri = new Uri("https://docs.ovhcloud.com/en/guides/public-cloud/ai-machine-learning/ai-endpoints-getting-started/"),
        RequestProfile = ChatRequestProfile,
        Capabilities = new Dictionary<string, object?>
        {
            ["SupportsStreaming"] = true,
            ["SupportsFunctionCalling"] = true,
            ["SupportsJsonResponseFormat"] = true,
            ["OpenAICompatibleEndpoint"] = "https://oai.endpoints.kepler.ai.cloud.ovh.net/v1/"
        }
    };

    protected override OpenAICompatibleProviderDefinition Definition => ProviderDefinition;
    protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<OVHcloudProviderConfig> ConfigurationTypeInfo => OVHcloudJsonContext.Default.OVHcloudProviderConfig;

    public override IProviderErrorHandler CreateErrorHandler()
        => new OpenAICompatibleErrorHandler(DisplayName);
}

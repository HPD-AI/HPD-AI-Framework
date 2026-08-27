using HPD.Agent.Providers;
using HPD.Agent.Providers.OVHcloud;
using System;

namespace HPD.Agent;

public static class OVHcloudAgentBuilderExtensions
{
    public static AgentBuilder WithOVHcloud(
        this AgentBuilder builder,
        string model = OVHcloudProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for OVHcloud AI Endpoints provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "ovhcloud",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "ovhcloud:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new OVHcloudProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures OVHcloud with a literal runtime-only API key.</summary>
    public static AgentBuilder WithOVHcloud(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithOVHcloud(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}

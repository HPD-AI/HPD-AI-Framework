using HPD.Agent.Providers;
using HPD.Agent.Providers.OVHcloud;
using System;

namespace HPD.Agent;

public static class OVHcloudAgentBuilderExtensions
{
    public static AgentBuilder WithOVHcloud(
        this AgentBuilder builder,
        string model = OVHcloudProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for OVHcloud AI Endpoints provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "ovhcloud",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

using HPD.Agent.Providers;
using HPD.Agent.Providers.Scaleway;
using System;

namespace HPD.Agent;

public static class ScalewayAgentBuilderExtensions
{
    public static AgentBuilder WithScaleway(
        this AgentBuilder builder,
        string model = ScalewayProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Scaleway Generative APIs provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "scaleway",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

using HPD.Agent.Providers;
using HPD.Agent.Providers.Nebius;
using System;

namespace HPD.Agent;

public static class NebiusAgentBuilderExtensions
{
    public static AgentBuilder WithNebius(
        this AgentBuilder builder,
        string model = NebiusProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Nebius Token Factory provider.", nameof(model));
        }

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "nebius",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

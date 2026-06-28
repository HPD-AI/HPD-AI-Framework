using HPD.Agent.Providers;
using HPD.Agent.Providers.Cerebras;
using System;

namespace HPD.Agent;

public static class CerebrasAgentBuilderExtensions
{
    public static AgentBuilder WithCerebras(
        this AgentBuilder builder,
        string model = CerebrasProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Cerebras provider.", nameof(model));
        }

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "cerebras",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

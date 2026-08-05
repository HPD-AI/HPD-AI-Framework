using HPD.Agent.Providers;
using HPD.Agent.Providers.MiniMax;
using System;

namespace HPD.Agent;

public static class MiniMaxAgentBuilderExtensions
{
    public static AgentBuilder WithMiniMax(
        this AgentBuilder builder,
        string model = MiniMaxProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for MiniMax provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "minimax",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new MiniMaxProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

using HPD.Agent.Providers;
using HPD.Agent.Providers.Hyperbolic;
using System;

namespace HPD.Agent;

public static class HyperbolicAgentBuilderExtensions
{
    public static AgentBuilder WithHyperbolic(
        this AgentBuilder builder,
        string model = HyperbolicProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Hyperbolic provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "hyperbolic",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new HyperbolicProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

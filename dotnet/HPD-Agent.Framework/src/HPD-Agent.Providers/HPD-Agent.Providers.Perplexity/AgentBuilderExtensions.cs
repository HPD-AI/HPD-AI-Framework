using HPD.Agent.Providers;
using HPD.Agent.Providers.Perplexity;
using System;

namespace HPD.Agent;

public static class PerplexityAgentBuilderExtensions
{
    public static AgentBuilder WithPerplexity(
        this AgentBuilder builder,
        string model = PerplexityProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Perplexity provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "perplexity",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new PerplexityProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

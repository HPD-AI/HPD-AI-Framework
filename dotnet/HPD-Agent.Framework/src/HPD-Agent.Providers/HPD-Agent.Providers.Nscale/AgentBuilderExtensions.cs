using HPD.Agent.Providers;
using HPD.Agent.Providers.Nscale;
using System;

namespace HPD.Agent;

public static class NscaleAgentBuilderExtensions
{
    public static AgentBuilder WithNscale(
        this AgentBuilder builder,
        string model = NscaleProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Nscale provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "nscale",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new NscaleProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

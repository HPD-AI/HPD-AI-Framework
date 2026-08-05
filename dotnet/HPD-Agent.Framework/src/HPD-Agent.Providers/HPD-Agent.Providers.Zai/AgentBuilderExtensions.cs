using HPD.Agent.Providers;
using HPD.Agent.Providers.Zai;
using System;

namespace HPD.Agent;

public static class ZaiAgentBuilderExtensions
{
    public static AgentBuilder WithZai(
        this AgentBuilder builder,
        string model = ZaiProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Z.AI provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "zai",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new ZaiProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

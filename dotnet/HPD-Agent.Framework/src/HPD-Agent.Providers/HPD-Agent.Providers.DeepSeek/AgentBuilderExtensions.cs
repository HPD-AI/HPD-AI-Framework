using HPD.Agent.Providers;
using HPD.Agent.Providers.DeepSeek;
using System;

namespace HPD.Agent;

public static class DeepSeekAgentBuilderExtensions
{
    public static AgentBuilder WithDeepSeek(
        this AgentBuilder builder,
        string model = DeepSeekProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for DeepSeek provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "deepseek",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new DeepSeekProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

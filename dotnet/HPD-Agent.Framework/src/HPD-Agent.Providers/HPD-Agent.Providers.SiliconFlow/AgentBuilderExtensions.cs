using HPD.Agent.Providers;
using HPD.Agent.Providers.SiliconFlow;
using System;

namespace HPD.Agent;

public static class SiliconFlowAgentBuilderExtensions
{
    public static AgentBuilder WithSiliconFlow(
        this AgentBuilder builder,
        string model = SiliconFlowProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for SiliconFlow provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "siliconflow",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new SiliconFlowProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

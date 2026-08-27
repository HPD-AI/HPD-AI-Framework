using HPD.Agent.Providers;
using HPD.Agent.Providers.LMStudio;
using System;

namespace HPD.Agent;

public static class LMStudioAgentBuilderExtensions
{
    public static AgentBuilder WithLMStudio(
        this AgentBuilder builder,
        string model = LMStudioProvider.DefaultChatModel,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for LM Studio provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "lmstudio",
                Authentication = new AnonymousProviderAuthentication()
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new LMStudioProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

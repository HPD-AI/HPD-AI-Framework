using HPD.Agent.Providers;
using HPD.Agent.Providers.SambaNova;
using System;

namespace HPD.Agent;

public static class SambaNovaAgentBuilderExtensions
{
    public static AgentBuilder WithSambaNova(
        this AgentBuilder builder,
        string model = SambaNovaProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for SambaNova provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "sambanova",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new SambaNovaProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

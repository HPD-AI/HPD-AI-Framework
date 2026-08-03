using HPD.Agent.Providers;
using HPD.Agent.Providers.Venice;
using System;

namespace HPD.Agent;

public static class VeniceAgentBuilderExtensions
{
    public static AgentBuilder WithVenice(
        this AgentBuilder builder,
        string model = VeniceProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Venice.ai provider.", nameof(model));
        }

        var chatConfig = new ProviderClientConfig
        {
            ProviderKey = "venice",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

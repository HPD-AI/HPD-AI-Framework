using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Fireworks;

/// <summary>
/// Extension methods for AgentBuilder to configure Fireworks AI as the chat provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Fireworks AI for chat.
    /// </summary>
    public static AgentBuilder WithFireworks(
        this AgentBuilder builder,
        string model = "accounts/fireworks/models/llama-v3p1-8b-instruct",
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Fireworks AI provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "fireworks",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new FireworksProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

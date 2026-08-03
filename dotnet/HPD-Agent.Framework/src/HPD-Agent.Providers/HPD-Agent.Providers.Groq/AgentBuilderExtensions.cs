using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Groq;

/// <summary>
/// Extension methods for AgentBuilder to configure Groq.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Groq as the chat provider.
    /// </summary>
    public static AgentBuilder WithGroq(
        this AgentBuilder builder,
        string model = "llama-3.3-70b-versatile",
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Groq provider.", nameof(model));

        var chatConfig = new ProviderClientConfig
        {
            ProviderKey = "groq",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

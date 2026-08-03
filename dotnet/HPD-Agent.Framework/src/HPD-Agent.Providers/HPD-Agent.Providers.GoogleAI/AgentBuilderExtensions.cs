using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Extension methods for AgentBuilder to configure Google AI.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Google AI as the chat provider.
    /// </summary>
    public static AgentBuilder WithGoogleAI(
        this AgentBuilder builder,
        string? apiKey = null,
        string model = "gemini-2.0-flash",
        Action<GoogleAIProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Google AI provider.", nameof(model));

        var providerConfig = new GoogleAIProviderConfig();
        configure?.Invoke(providerConfig);

        var chatConfig = new ProviderClientConfig
        {
            ProviderKey = "google-ai",
            ApiKey = apiKey,
            ModelName = model
        };
        chatConfig.SetProviderConfig(providerConfig);

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

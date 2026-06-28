using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.DeepInfra;

/// <summary>
/// Extension methods for AgentBuilder to configure DeepInfra as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use DeepInfra as the chat provider.
    /// </summary>
    public static AgentBuilder WithDeepInfra(
        this AgentBuilder builder,
        string model = DeepInfraProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for DeepInfra provider.", nameof(model));

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "deepinfra",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

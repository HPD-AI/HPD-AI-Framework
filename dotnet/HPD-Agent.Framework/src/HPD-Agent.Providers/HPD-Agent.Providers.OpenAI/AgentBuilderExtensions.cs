using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Extension methods for AgentBuilder to configure OpenAI providers.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use OpenAI as the chat provider.
    /// </summary>
    public static AgentBuilder WithOpenAI(
        this AgentBuilder builder,
        string model,
        string? apiKey = null,
        Action<OpenAIProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for OpenAI provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "openai",
            ApiKey = apiKey,
            ModelName = model
        };

        var providerConfig = new OpenAIProviderConfig();
        configure?.Invoke(providerConfig);
        chatConfig.SetProviderConfig(providerConfig);

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>
    /// Configures the agent to use traditional Azure OpenAI endpoints.
    /// </summary>
    public static AgentBuilder WithAzureOpenAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        string? apiKey = null,
        Action<AzureOpenAIProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required for Azure OpenAI provider.", nameof(endpoint));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Azure OpenAI provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "azure-openai",
            Endpoint = endpoint,
            ApiKey = apiKey,
            ModelName = model
        };

        var providerConfig = new AzureOpenAIProviderConfig();
        configure?.Invoke(providerConfig);
        chatConfig.SetProviderConfig(providerConfig);

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

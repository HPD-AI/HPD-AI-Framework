using HPD.Agent;

namespace HPD.Agent.Providers.Moonshot;

/// <summary>
/// Extension methods for AgentBuilder to configure Moonshot as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Moonshot/Kimi as the chat provider.
    /// </summary>
    public static AgentBuilder WithMoonshot(
        this AgentBuilder builder,
        string model = MoonshotProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Moonshot provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "moonshot",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds Moonshot/Kimi-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithMoonshotChatRequestOptions(
        this AgentBuilder builder,
        MoonshotChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        options.ApplyTo(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds Moonshot/Kimi-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithMoonshotChatRequestOptions(
        this AgentBuilder builder,
        Action<MoonshotChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MoonshotChatRequestOptions();
        configure(options);
        return builder.WithMoonshotChatRequestOptions(options);
    }
}

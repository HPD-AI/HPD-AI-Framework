using System.Collections.Generic;
using HPD.Agent;
using HPD.Agent.Providers;

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
        string? endpoint = null,
        Action<MoonshotProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Moonshot provider.", nameof(model));

        var providerConfig = new MoonshotProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "moonshot",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(MoonshotProviderConfig config, Action<MoonshotProviderConfig>? configure)
    {
        var errors = new List<string>();
        MoonshotProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}

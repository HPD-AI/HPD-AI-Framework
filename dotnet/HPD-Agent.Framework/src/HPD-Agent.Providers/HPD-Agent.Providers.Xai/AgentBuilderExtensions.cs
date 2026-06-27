using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Xai;

/// <summary>
/// Extension methods for AgentBuilder to configure xAI.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use xAI as the chat provider.
    /// </summary>
    public static AgentBuilder WithXai(
        this AgentBuilder builder,
        string model = XaiProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<XaiProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for xAI provider.", nameof(model));

        var providerConfig = new XaiProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "xai",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(XaiProviderConfig config, Action<XaiProviderConfig>? configure)
    {
        var errors = new List<string>();
        XaiProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}

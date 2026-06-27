using System;
using System.Collections.Generic;
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
        string? endpoint = null,
        Action<FireworksProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Fireworks AI provider.", nameof(model));

        var providerConfig = new FireworksProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "fireworks",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(FireworksProviderConfig config, Action<FireworksProviderConfig>? configure)
    {
        var errors = new List<string>();
        FireworksProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}

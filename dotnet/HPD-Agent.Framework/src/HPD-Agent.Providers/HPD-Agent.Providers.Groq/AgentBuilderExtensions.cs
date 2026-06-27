using System;
using System.Collections.Generic;
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
        string? endpoint = null,
        Action<GroqProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Groq provider.", nameof(model));

        var providerConfig = new GroqProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "groq",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(GroqProviderConfig config, Action<GroqProviderConfig>? configure)
    {
        var errors = new List<string>();
        GroqProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}

using HPD.Agent.Providers;
using HPD.Agent.Providers.Perplexity;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class PerplexityAgentBuilderExtensions
{
    public static AgentBuilder WithPerplexity(
        this AgentBuilder builder,
        string model = PerplexityProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<PerplexityProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Perplexity provider.", nameof(model));
        }

        var providerConfig = new PerplexityProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "perplexity",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(PerplexityProviderConfig config, Action<PerplexityProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

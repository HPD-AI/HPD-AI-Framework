using HPD.Agent.Providers;
using HPD.Agent.Providers.Hyperbolic;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class HyperbolicAgentBuilderExtensions
{
    public static AgentBuilder WithHyperbolic(
        this AgentBuilder builder,
        string model = HyperbolicProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<HyperbolicProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Hyperbolic provider.", nameof(model));
        }

        var providerConfig = new HyperbolicProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "hyperbolic",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(HyperbolicProviderConfig config, Action<HyperbolicProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

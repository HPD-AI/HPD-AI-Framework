using HPD.Agent.Providers;
using HPD.Agent.Providers.Nebius;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class NebiusAgentBuilderExtensions
{
    public static AgentBuilder WithNebius(
        this AgentBuilder builder,
        string model = NebiusProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<NebiusProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Nebius Token Factory provider.", nameof(model));
        }

        var providerConfig = new NebiusProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "nebius",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(NebiusProviderConfig config, Action<NebiusProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

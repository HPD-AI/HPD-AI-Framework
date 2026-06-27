using HPD.Agent.Providers;
using HPD.Agent.Providers.Scaleway;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class ScalewayAgentBuilderExtensions
{
    public static AgentBuilder WithScaleway(
        this AgentBuilder builder,
        string model = ScalewayProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<ScalewayProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Scaleway Generative APIs provider.", nameof(model));
        }

        var providerConfig = new ScalewayProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "scaleway",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(ScalewayProviderConfig config, Action<ScalewayProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

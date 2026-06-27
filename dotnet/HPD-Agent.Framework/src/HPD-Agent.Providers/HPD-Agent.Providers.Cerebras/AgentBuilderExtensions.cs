using HPD.Agent.Providers;
using HPD.Agent.Providers.Cerebras;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class CerebrasAgentBuilderExtensions
{
    public static AgentBuilder WithCerebras(
        this AgentBuilder builder,
        string model = CerebrasProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<CerebrasProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Cerebras provider.", nameof(model));
        }

        var providerConfig = new CerebrasProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "cerebras",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(CerebrasProviderConfig config, Action<CerebrasProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

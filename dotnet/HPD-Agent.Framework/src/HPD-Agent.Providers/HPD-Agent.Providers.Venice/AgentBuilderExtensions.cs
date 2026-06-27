using HPD.Agent.Providers;
using HPD.Agent.Providers.Venice;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class VeniceAgentBuilderExtensions
{
    public static AgentBuilder WithVenice(
        this AgentBuilder builder,
        string model = VeniceProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<VeniceProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Venice.ai provider.", nameof(model));
        }

        var providerConfig = new VeniceProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "venice",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(VeniceProviderConfig config, Action<VeniceProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

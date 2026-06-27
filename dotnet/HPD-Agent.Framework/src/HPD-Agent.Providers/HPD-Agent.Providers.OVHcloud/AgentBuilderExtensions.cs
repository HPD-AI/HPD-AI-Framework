using HPD.Agent.Providers;
using HPD.Agent.Providers.OVHcloud;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class OVHcloudAgentBuilderExtensions
{
    public static AgentBuilder WithOVHcloud(
        this AgentBuilder builder,
        string model = OVHcloudProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<OVHcloudProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for OVHcloud AI Endpoints provider.", nameof(model));
        }

        var providerConfig = new OVHcloudProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "ovhcloud",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(OVHcloudProviderConfig config, Action<OVHcloudProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

using HPD.Agent.Providers;
using HPD.Agent.Providers.SiliconFlow;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class SiliconFlowAgentBuilderExtensions
{
    public static AgentBuilder WithSiliconFlow(
        this AgentBuilder builder,
        string model = SiliconFlowProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<SiliconFlowProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for SiliconFlow provider.", nameof(model));
        }

        var providerConfig = new SiliconFlowProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "siliconflow",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(SiliconFlowProviderConfig config, Action<SiliconFlowProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

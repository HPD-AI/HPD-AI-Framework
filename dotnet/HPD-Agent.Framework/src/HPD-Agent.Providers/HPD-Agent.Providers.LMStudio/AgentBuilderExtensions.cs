using HPD.Agent.Providers;
using HPD.Agent.Providers.LMStudio;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class LMStudioAgentBuilderExtensions
{
    public static AgentBuilder WithLMStudio(
        this AgentBuilder builder,
        string model = LMStudioProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<LMStudioProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for LM Studio provider.", nameof(model));
        }

        var providerConfig = new LMStudioProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "lmstudio",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(LMStudioProviderConfig config, Action<LMStudioProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

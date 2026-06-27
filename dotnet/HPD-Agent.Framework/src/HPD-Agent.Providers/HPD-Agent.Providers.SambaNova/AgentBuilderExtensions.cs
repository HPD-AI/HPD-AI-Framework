using HPD.Agent.Providers;
using HPD.Agent.Providers.SambaNova;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class SambaNovaAgentBuilderExtensions
{
    public static AgentBuilder WithSambaNova(
        this AgentBuilder builder,
        string model = SambaNovaProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<SambaNovaProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for SambaNova provider.", nameof(model));
        }

        var providerConfig = new SambaNovaProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "sambanova",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(SambaNovaProviderConfig config, Action<SambaNovaProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

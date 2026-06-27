using HPD.Agent.Providers;
using HPD.Agent.Providers.NvidiaNim;
using HPD.Agent.Providers.OpenAICompatible;
using System;
using System.Collections.Generic;

namespace HPD.Agent;

public static class NvidiaNimAgentBuilderExtensions
{
    public static AgentBuilder WithNvidiaNim(
        this AgentBuilder builder,
        string model = NvidiaNimProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<NvidiaNimProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for NVIDIA NIM provider.", nameof(model));
        }

        var providerConfig = new NvidiaNimProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "nvidia-nim",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(NvidiaNimProviderConfig config, Action<NvidiaNimProviderConfig>? configure)
    {
        var errors = new List<string>();
        OpenAICompatibleChatOptionsDefaults.Validate(config, errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
        }
    }
}

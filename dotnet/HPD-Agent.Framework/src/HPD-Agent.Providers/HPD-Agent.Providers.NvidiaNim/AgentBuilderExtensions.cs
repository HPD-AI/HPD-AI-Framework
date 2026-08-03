using HPD.Agent.Providers;
using HPD.Agent.Providers.NvidiaNim;
using System;

namespace HPD.Agent;

public static class NvidiaNimAgentBuilderExtensions
{
    public static AgentBuilder WithNvidiaNim(
        this AgentBuilder builder,
        string model = NvidiaNimProvider.DefaultChatModel,
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for NVIDIA NIM provider.", nameof(model));
        }

        var chatConfig = new ProviderClientConfig
        {
            ProviderKey = "nvidia-nim",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
}

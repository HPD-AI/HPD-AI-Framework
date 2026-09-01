using HPD.Agent.Providers;
using HPD.Agent.Providers.NvidiaNim;
using System;

namespace HPD.Agent;

public static class NvidiaNimAgentBuilderExtensions
{
    public static AgentBuilder WithNvidiaNim(
        this AgentBuilder builder,
        string model = NvidiaNimProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for NVIDIA NIM provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "nvidia-nim",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "nvidia-nim:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new NvidiaNimProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures NVIDIA NIM with a literal runtime-only API key.</summary>
    public static AgentBuilder WithNvidiaNim(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithNvidiaNim(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}

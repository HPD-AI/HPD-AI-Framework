using System;
using System.Collections.Generic;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Replicate;

/// <summary>
/// Extension methods for AgentBuilder to configure Replicate.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Replicate as the image generation provider.
    /// </summary>
    public static AgentBuilder WithReplicateImageGeneration(
        this AgentBuilder builder,
        string model = ReplicateProvider.DefaultModel,
        string? apiKey = null,
        string? endpoint = null,
        Action<ReplicateProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Replicate image generation.", nameof(model));

        var providerConfig = new ReplicateProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var imageConfig = new ProviderClientConfig
        {
            ProviderKey = "replicate",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.ImageGeneration, imageConfig);
        imageConfig.SetProviderConfig(providerConfig, ProviderClientFamily.ImageGeneration);

        return builder;
    }

    private static void ValidateProviderConfig(ReplicateProviderConfig config, Action<ReplicateProviderConfig>? configure)
    {
        var errors = new List<string>();
        ReplicateProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}

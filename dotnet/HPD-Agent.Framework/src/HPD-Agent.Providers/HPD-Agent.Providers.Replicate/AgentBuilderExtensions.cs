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
        string? mediaType = null,
        Action<ReplicateProviderConfig>? configureClient = null,
        Action<ReplicateImageOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Replicate image generation.", nameof(model));

        var providerConfig = new ReplicateProviderConfig();
        configureClient?.Invoke(providerConfig);
        var providerOptions = new ReplicateImageOptions();
        configureOptions?.Invoke(providerOptions);
        ValidateProviderOptions(providerOptions, configureOptions);

        var imageConfig = new ImageGenerationClientConfig
        {
            ProviderKey = "replicate",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model,
            MediaType = mediaType,
            ProviderConfig = providerConfig,
            ProviderOptions = providerOptions
        };

        builder.Config.SetClientConfig(ProviderClientFamily.ImageGeneration, imageConfig);
        return builder;
    }

    private static void ValidateProviderOptions(ReplicateImageOptions config, Action<ReplicateImageOptions>? configure)
    {
        var errors = new List<string>();
        ReplicateProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}

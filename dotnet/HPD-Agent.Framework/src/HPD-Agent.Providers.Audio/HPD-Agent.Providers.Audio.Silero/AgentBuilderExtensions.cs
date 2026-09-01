// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Audio.Silero;

/// <summary>Extension methods for configuring Silero audio provider families.</summary>
public static class AgentBuilderExtensions
{
    /// <summary>Configures Silero as the voice-activity provider.</summary>
    /// <param name="builder">The agent builder to configure.</param>
    /// <param name="modelPath">The explicit local path to the pinned Silero ONNX model.</param>
    /// <param name="model">The model identifier, or the provider default when omitted.</param>
    /// <param name="modelSha256">The expected lowercase SHA-256 digest, or the official digest when omitted.</param>
    /// <param name="configure">An optional callback for provider-specific execution options.</param>
    /// <returns>The same builder instance.</returns>
    public static AgentBuilder WithSileroVoiceActivity(
        this AgentBuilder builder,
        string modelPath,
        string? model = null,
        string? modelSha256 = null,
        Action<SileroVadOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var options = new SileroVadOptions
        {
            ModelPath = modelPath,
            ModelSha256 = modelSha256 ?? SileroModelArtifactV1.OfficialSha256
        };
        configure?.Invoke(options);

        builder.Config.SetClientConfig(
            ProviderClientFamily.VoiceActivityDetection,
            new VoiceActivityClientConfig
            {
                Provider = new ProviderReference
                {
                    Key = SileroAudioProvider.Key,
                    Authentication = new AnonymousProviderAuthentication()
                },
                ModelName = model ?? SileroAudioProvider.DefaultModel,
                ProviderConfig = options
            });
        return builder;
    }
}

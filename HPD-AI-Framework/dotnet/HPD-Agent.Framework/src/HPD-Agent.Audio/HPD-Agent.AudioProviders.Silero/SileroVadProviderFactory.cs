// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Reflection;
using System.Text.Json;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Vad;
using Microsoft.ML.OnnxRuntime;

namespace HPD.Agent.AudioProviders.Silero;

/// <summary>
/// Creates SileroVadDetector instances from VadConfig.
/// Loads the embedded silero_vad.onnx model once and reuses the session.
/// </summary>
public class SileroVadProviderFactory : IVadProviderFactory
{
    private const string ModelResourceName =
        "HPD.Agent.AudioProviders.Silero.Resources.silero_vad.onnx";

    public IVoiceActivityDetector CreateDetector(VadConfig config, IServiceProvider? services = null)
    {
        var sileroConfig = string.IsNullOrEmpty(config.ProviderOptionsJson)
            ? new SileroVadConfig()
            : JsonSerializer.Deserialize<SileroVadConfig>(config.ProviderOptionsJson)
              ?? new SileroVadConfig();

        var session = LoadSession(sileroConfig.ForceCpu);
        return new SileroVadDetector(session, config, sileroConfig);
    }

    public VadProviderMetadata GetMetadata() => new()
    {
        ProviderKey = "silero-vad",
        DisplayName = "Silero VAD",
        SupportedFormats = ["pcm-16bit-8khz", "pcm-16bit-16khz"],
        DocumentationUrl = "https://github.com/snakers4/silero-vad",
        CustomProperties = new Dictionary<string, object>
        {
            ["inference"] = "local-onnx",
            ["supported_sample_rates"] = new[] { 8000, 16000 }
        }
    };

    public ValidationResult Validate(VadConfig config)
    {
        var errors = new List<string>();

        if (config.ActivationThreshold is < 0f or > 1f)
            errors.Add("ActivationThreshold must be between 0.0 and 1.0");

        if (!string.IsNullOrEmpty(config.ProviderOptionsJson))
        {
            try
            {
                var sileroConfig = JsonSerializer.Deserialize<SileroVadConfig>(
                    config.ProviderOptionsJson);

                if (sileroConfig?.SampleRate is not (8000 or 16000))
                    errors.Add("SileroVadConfig.SampleRate must be 8000 or 16000");

                if (sileroConfig?.ModelResetIntervalSeconds <= 0)
                    errors.Add("SileroVadConfig.ModelResetIntervalSeconds must be positive");

                if (sileroConfig?.DeactivationThreshold is float dt and (< 0f or > 1f))
                    errors.Add($"SileroVadConfig.DeactivationThreshold ({dt}) must be between 0.0 and 1.0");
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid ProviderOptionsJson: {ex.Message}");
            }
        }

        // Verify model is accessible
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ModelResourceName);
        if (stream == null)
            errors.Add($"Silero VAD model not found as embedded resource '{ModelResourceName}'. " +
                       "Ensure the project was built correctly.");

        return errors.Count > 0
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    // -------------------------------------------------------------------------

    private static InferenceSession LoadSession(bool forceCpu)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ModelResourceName)
            ?? throw new InvalidOperationException(
                $"Silero VAD ONNX model not found as embedded resource '{ModelResourceName}'. " +
                "Ensure the project was built with the model file included.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var modelBytes = ms.ToArray();

        var opts = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1
        };

        if (forceCpu)
            opts.AppendExecutionProvider_CPU();

        return new InferenceSession(modelBytes, opts);
    }
}

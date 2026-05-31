// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Reflection;
using System.Text.Json;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Vad;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.ML.OnnxRuntime;

namespace HPD.Agent.AudioProviders.Silero;

/// <summary>
/// Creates SileroVadDetector instances.
/// Loads the embedded silero_vad.onnx model once and reuses the session.
/// </summary>
public class SileroVadProvider : IVoiceActivityDetectorProvider
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

    public string ProviderKey => "silero-vad";
    public string DisplayName => "Silero VAD";

    public IVoiceActivityDetector CreateVoiceActivityDetector(
        ClientProviderConfig config,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null)
    {
        var vadConfig = new VadConfig
        {
            Provider = ProviderKey,
            ProviderOptionsJson = config.ProviderOptionsJson
        };

        if (config.AdditionalProperties != null)
        {
            if (config.AdditionalProperties.TryGetValue("activationThreshold", out var activationThreshold) &&
                TryGetSingle(activationThreshold, out var threshold))
                vadConfig.ActivationThreshold = threshold;

            if (config.AdditionalProperties.TryGetValue("minSpeechDuration", out var minSpeechDuration) &&
                TryGetSingle(minSpeechDuration, out var minSpeech))
                vadConfig.MinSpeechDuration = minSpeech;

            if (config.AdditionalProperties.TryGetValue("minSilenceDuration", out var minSilenceDuration) &&
                TryGetSingle(minSilenceDuration, out var minSilence))
                vadConfig.MinSilenceDuration = minSilence;

            if (config.AdditionalProperties.TryGetValue("prefixPaddingDuration", out var prefixPaddingDuration) &&
                TryGetSingle(prefixPaddingDuration, out var prefixPadding))
                vadConfig.PrefixPaddingDuration = prefixPadding;
        }

        return CreateDetector(vadConfig, services);
    }

    public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

    ProviderMetadata IProvider.GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        DocumentationUri = new Uri("https://github.com/snakers4/silero-vad"),
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.VoiceActivityDetection] = new()
            {
                Family = ProviderClientFamily.VoiceActivityDetection,
                Lifetime = ProviderFamilyLifetime.StatefulPerAudioSession,
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsAudio"] = true
                }
            }
        }
    };

    public ProviderValidationResult ValidateConfiguration(
        ClientProviderConfig config,
        ProviderClientFamily family)
    {
        if (family != ProviderClientFamily.VoiceActivityDetection)
            return ProviderValidationResult.Failure($"Silero VAD does not support provider family '{family}'.");

        var result = Validate(new VadConfig
        {
            Provider = ProviderKey,
            ProviderOptionsJson = config.ProviderOptionsJson
        });

        return result.IsValid
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(result.Errors.ToArray());
    }

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

    private static bool TryGetSingle(object value, out float result)
    {
        switch (value)
        {
            case float f:
                result = f;
                return true;
            case double d:
                result = (float)d;
                return true;
            case decimal m:
                result = (float)m;
                return true;
            case int i:
                result = i;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetSingle(out result);
            default:
                result = 0;
                return false;
        }
    }
}

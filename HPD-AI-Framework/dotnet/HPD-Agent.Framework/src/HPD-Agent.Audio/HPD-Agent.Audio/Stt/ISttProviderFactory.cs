// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Recognition;

namespace HPD.Agent.Audio.Stt;

/// <summary>
/// Factory for creating HPD speech recognizers.
/// Registered via SttProviderDiscovery in module initializer.
/// </summary>
public interface ISttProviderFactory
{
    /// <summary>
    /// Creates a speech recognizer from configuration.
    /// </summary>
    ISpeechRecognizer CreateRecognizer(SttConfig config, IServiceProvider? services = null);

    /// <summary>
    /// Gets metadata about this STT provider's capabilities.
    /// </summary>
    SttProviderMetadata GetMetadata();

    /// <summary>
    /// Validates STT configuration for this provider.
    /// </summary>
    ValidationResult Validate(SttConfig config);
}

// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Stt;

using HPD.Agent.Audio.Recognition;

/// <summary>
/// Metadata about an STT provider's capabilities.
/// </summary>
public class SttProviderMetadata
{
    /// <summary>
    /// Provider registration key.
    /// </summary>
    public string ProviderKey { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable provider name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Truthful recognition capabilities for recognizers created by this provider.
    /// </summary>
    public SpeechRecognitionCapabilities Capabilities { get; init; } = new();

    /// <summary>
    /// Supported language tags, or <see langword="null"/> when unrestricted or provider-defined.
    /// </summary>
    public string[]? SupportedLanguages { get; init; }

    /// <summary>
    /// Supported audio input formats.
    /// </summary>
    public string[]? SupportedFormats { get; init; }

    /// <summary>
    /// Provider documentation URL.
    /// </summary>
    public string? DocumentationUrl { get; init; }

    /// <summary>
    /// Provider-specific metadata.
    /// </summary>
    public Dictionary<string, object>? CustomProperties { get; init; }
}

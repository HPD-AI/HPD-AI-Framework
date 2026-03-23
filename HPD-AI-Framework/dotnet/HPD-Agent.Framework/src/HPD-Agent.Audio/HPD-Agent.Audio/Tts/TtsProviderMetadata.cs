// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Tts;

/// <summary>
/// Metadata about a TTS provider's capabilities.
/// </summary>
public class TtsProviderMetadata
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool SupportsStreaming { get; init; } = false;
    public string[]? SupportedVoices { get; init; }
    public string[]? SupportedLanguages { get; init; }
    public string[]? SupportedFormats { get; init; }
    public string? DocumentationUrl { get; init; }
    public Dictionary<string, object>? CustomProperties { get; init; }
}

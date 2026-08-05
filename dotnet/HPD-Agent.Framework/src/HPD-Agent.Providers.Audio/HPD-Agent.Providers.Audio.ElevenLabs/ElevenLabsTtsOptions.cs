// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

/// <summary>ElevenLabs-specific text-to-speech operation options.</summary>
public sealed class ElevenLabsTtsOptions : global::HPD.Agent.ITextToSpeechProviderOptions
{
    /// <summary>Gets or sets voice stability.</summary>
    public double? Stability { get; set; }
    /// <summary>Gets or sets similarity boost.</summary>
    public double? SimilarityBoost { get; set; }
    /// <summary>Gets or sets style exaggeration.</summary>
    public double? Style { get; set; }
    /// <summary>Gets or sets whether speaker boost is enabled.</summary>
    public bool? UseSpeakerBoost { get; set; }
    /// <summary>Gets or sets the provider text-normalization mode.</summary>
    public string? ApplyTextNormalization { get; set; }
    /// <summary>Gets or sets websocket automatic streaming mode.</summary>
    public bool? AutoMode { get; set; }
    /// <summary>Gets or sets whether websocket alignment is synchronized.</summary>
    public bool? SyncAlignment { get; set; }
    /// <summary>Gets or sets websocket inactivity timeout.</summary>
    public int? InactivityTimeout { get; set; }
}

// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Metadata about an EOT provider's capabilities.
/// </summary>
public class EotProviderMetadata
{
    /// <summary>Provider key used in <see cref="EotConfig.Provider"/>.</summary>
    public string ProviderKey { get; init; } = string.Empty;

    /// <summary>Human-readable provider name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Optional documentation URL for the provider.</summary>
    public string? DocumentationUrl { get; init; }

    /// <summary>Provider-specific metadata.</summary>
    public Dictionary<string, object>? CustomProperties { get; init; }
}

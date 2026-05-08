// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Factory for creating end-of-turn detectors.
/// </summary>
public interface IEotProviderFactory
{
    /// <summary>Creates an EOT detector from configuration.</summary>
    IEotDetector CreateDetector(EotConfig config, IServiceProvider? services = null);

    /// <summary>Gets metadata about this EOT provider's capabilities.</summary>
    EotProviderMetadata GetMetadata();

    /// <summary>Validates EOT configuration for this provider.</summary>
    ValidationResult Validate(EotConfig config);
}

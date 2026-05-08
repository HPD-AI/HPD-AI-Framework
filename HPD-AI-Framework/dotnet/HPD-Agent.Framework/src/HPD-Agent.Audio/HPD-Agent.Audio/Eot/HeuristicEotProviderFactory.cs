// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Built-in heuristic EOT provider.
/// </summary>
public sealed class HeuristicEotProviderFactory : IEotProviderFactory
{
    /// <inheritdoc />
    public IEotDetector CreateDetector(EotConfig config, IServiceProvider? services = null)
    {
        config.Validate();
        return new HeuristicEotDetector(config);
    }

    /// <inheritdoc />
    public EotProviderMetadata GetMetadata() => new()
    {
        ProviderKey = "heuristic-eot",
        DisplayName = "Heuristic End-of-Turn"
    };

    /// <inheritdoc />
    public ValidationResult Validate(EotConfig config)
    {
        try
        {
            config.Validate();
            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure(ex.Message);
        }
    }
}

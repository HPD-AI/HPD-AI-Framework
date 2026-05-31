// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Eot;

using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;

/// <summary>
/// Built-in heuristic EOT provider.
/// </summary>
public sealed class HeuristicEotProvider : IEndOfTurnDetectorProvider
{
    public string ProviderKey => "heuristic-eot";
    public string DisplayName => "Heuristic End-of-Turn";

    /// <inheritdoc />
    public IEotDetector CreateDetector(EotConfig config, IServiceProvider? services = null)
    {
        config.Validate();
        return new HeuristicEotDetector(config);
    }

    public IEotDetector CreateEndOfTurnDetector(
        ClientProviderConfig config,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null)
    {
        return CreateDetector(new EotConfig
        {
            Provider = ProviderKey,
            ProviderOptionsJson = config.ProviderOptionsJson
        }, services);
    }

    public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

    ProviderMetadata IProvider.GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.EndOfTurnDetection] = new()
            {
                Family = ProviderClientFamily.EndOfTurnDetection,
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
        if (family != ProviderClientFamily.EndOfTurnDetection)
            return ProviderValidationResult.Failure($"Heuristic EOT does not support provider family '{family}'.");

        var result = Validate(new EotConfig
        {
            Provider = ProviderKey,
            ProviderOptionsJson = config.ProviderOptionsJson
        });

        return result.IsValid
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(result.Errors.ToArray());
    }

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

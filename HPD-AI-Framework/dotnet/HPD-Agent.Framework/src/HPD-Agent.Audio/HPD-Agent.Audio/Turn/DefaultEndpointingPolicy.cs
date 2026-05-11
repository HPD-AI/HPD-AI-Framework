// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Conservative initial endpointing policy.
/// </summary>
public sealed class DefaultEndpointingPolicy : IEndpointingPolicy
{
    private readonly TurnControllerOptions _options;

    /// <summary>Creates the default endpointing policy.</summary>
    public DefaultEndpointingPolicy(TurnControllerOptions? options = null)
    {
        _options = options ?? new TurnControllerOptions();
    }

    /// <inheritdoc />
    public EndpointingDecision Decide(EndpointingPolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_options.Mode is EndpointingMode.Manual or EndpointingMode.RealtimeModel)
        {
            return new EndpointingDecision
            {
                Delay = TimeSpan.Zero,
                Reason = context.FallbackReason
            };
        }

        if (_options.Mode is EndpointingMode.Vad or EndpointingMode.Stt)
        {
            return new EndpointingDecision
            {
                ShouldCommitNow = _options.MinEndpointingDelay == TimeSpan.Zero,
                Delay = _options.MinEndpointingDelay,
                Reason = context.FallbackReason
            };
        }

        var probability = _options.EotDetector?.GetEndOfTurnProbability(context.Transcript.Text);
        if (probability >= _options.HighConfidenceThreshold)
        {
            return new EndpointingDecision
            {
                ShouldCommitNow = _options.MinEndpointingDelay == TimeSpan.Zero,
                Delay = _options.MinEndpointingDelay,
                EotProbability = probability,
                Reason = EndpointingReason.EotHighConfidence
            };
        }

        return new EndpointingDecision
        {
            ShouldCommitNow = _options.MinEndpointingDelay == TimeSpan.Zero,
            Delay = probability is null ? _options.MinEndpointingDelay : _options.MaxEndpointingDelay,
            EotProbability = probability,
            Reason = probability is null ? context.FallbackReason : EndpointingReason.EotUnlikelyMaxDelay
        };
    }
}

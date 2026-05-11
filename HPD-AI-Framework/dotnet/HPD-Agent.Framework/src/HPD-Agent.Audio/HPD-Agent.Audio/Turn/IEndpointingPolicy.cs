// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Decides whether and when a recognized turn should commit.
/// </summary>
public interface IEndpointingPolicy
{
    /// <summary>Computes an endpointing decision for the current turn.</summary>
    EndpointingDecision Decide(EndpointingPolicyContext context);
}

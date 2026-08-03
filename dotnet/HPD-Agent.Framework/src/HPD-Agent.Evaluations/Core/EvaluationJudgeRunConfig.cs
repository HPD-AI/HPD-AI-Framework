// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.Evaluations;

/// <summary>
/// Configuration for the Chat-family role used as an evaluation judge.
/// </summary>
public sealed class EvaluationJudgeRunConfig
{
    /// <summary>Gets the family inheritance behavior for the judge role.</summary>
    public ClientFamilyInheritanceMode Inheritance { get; init; } =
        ClientFamilyInheritanceMode.InheritResolved;

    /// <summary>Gets the optional judge-specific Chat configuration.</summary>
    public ChatClientConfig? Chat { get; init; }

    /// <summary>Gets the maximum duration of one judge call.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    internal EvaluationJudgeRunConfig Snapshot() => new()
    {
        Inheritance = Inheritance,
        Chat = Chat is null ? null : (ChatClientConfig)ProviderClientConfigResolver.Clone(Chat),
        Timeout = Timeout
    };
}

// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using HPD.Agent.Evaluations.Batch;

namespace HPD.Agent.Evaluations;

/// <summary>
/// Configuration for the LLM used as a judge in evaluation.
/// Prefer <see cref="OverrideAgent"/> for production so judge calls use the normal
/// HPD-Agent provider, retry, middleware, secrets, and observability pipeline.
/// <see cref="OverrideChatClient"/> remains as a low-level escape hatch for tests
/// and advanced embedding scenarios.
/// </summary>
public sealed class EvalJudgeConfig
{
    /// <summary>
    /// Per-judge call timeout in seconds. Cancels stuck judge LLM calls so they don't
    /// block the background evaluator task indefinitely.
    /// Default: 30 seconds. Override to 360+ for Azure Safety evaluators.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Direct IChatClient override.
    /// Use when you already have a resolved client (e.g. in tests, or when sharing
    /// a client across evaluators).
    /// </summary>
    [JsonIgnore]
    public IChatClient? OverrideChatClient { get; init; }

    /// <summary>
    /// Direct HPD agent override for agent-as-judge scenarios.
    /// Evaluation wraps calls to this agent in AgentRunConfig with
    /// IsInternalEvalJudgeCall = true and DisableEvaluators = true to prevent
    /// evaluation loops.
    /// </summary>
    [JsonIgnore]
    public IAgent? OverrideAgent { get; init; }
}

// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations;

/// <summary>
/// Configuration for the LLM used as a judge in evaluation.
/// Fully independent of the agent's own provider configuration.
/// Reuses ProviderConfig and ErrorHandlingConfig from AgentConfig for consistency.
/// </summary>
public sealed class EvalJudgeConfig
{
    /// <summary>
    /// Provider and model for the judge LLM. Reuses HPD.Agent.ProviderConfig —
    /// same ProviderKey, ModelName, ApiKey, Endpoint, and ProviderOptionsJson fields.
    /// If null, falls back to the agent's own provider.
    /// </summary>
    public ProviderConfig? Provider { get; init; }

    /// <summary>
    /// Per-judge call timeout in seconds. Cancels stuck judge LLM calls so they don't
    /// block the background evaluator task indefinitely.
    /// Default: 30 seconds. Override to 360+ for Azure Safety evaluators.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Retry configuration for HTTP 429 / 503 from the judge LLM.
    /// Reuses HPD.Agent.ErrorHandlingConfig — same MaxRetries, BackoffMultiplier,
    /// MaxRetryDelay, and UseProviderRetryDelays fields.
    /// Applied only to judge LLM calls, independently of the agent's own retry settings.
    /// If null, falls back to a sensible default (3 retries, 5s initial backoff).
    /// </summary>
    public ErrorHandlingConfig? RetryPolicy { get; init; }

    /// <summary>
    /// Direct IChatClient override. Takes priority over Provider.
    /// Use when you already have a resolved client (e.g. in tests, or when sharing
    /// a client across evaluators).
    /// </summary>
    [JsonIgnore]
    public IChatClient? OverrideChatClient { get; init; }
}

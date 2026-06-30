// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations;

/// <summary>
/// Response-only agent contract for LLM-as-judge calls.
/// </summary>
public interface IJudgeAgent
{
    Task<ChatResponse> RunAsync(AgentRunConfig config, CancellationToken ct = default);
}

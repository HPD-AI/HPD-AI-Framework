// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Storage;

/// <summary>
/// Captures one judge-model call made while evaluating an agent response.
/// This is evaluation trace data, separate from the evaluated agent's thread history.
/// </summary>
public sealed record JudgeCallRecord(
    string EvaluatorName,
    string Phase,
    IReadOnlyList<ChatMessage> Prompt,
    ChatResponse? Response,
    string? ModelId,
    UsageDetails? Usage,
    TimeSpan Duration,
    bool Succeeded,
    string? ErrorMessage);

// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.AspNetCore.Tests.TestInfrastructure;

/// <summary>
/// Builder helpers for ScoreRecord used across eval integration tests.
/// </summary>
internal static class ScoreRecordFactory
{
    /// <summary>
    /// Creates a ScoreRecord with a BooleanMetric that passes (true) by default.
    /// </summary>
    internal static ScoreRecord Make(
        string evaluatorName = "TestEvaluator",
        string evaluatorVersion = "1.0",
        string sessionId = "session-1",
        string branchId = "main",
        int turnIndex = 0,
        string agentName = "test-agent",
        bool passing = true,
        DateTimeOffset? createdAt = null,
        EvaluationSource source = EvaluationSource.Test,
        string? modelId = null)
    {
        var metric = new BooleanMetric(passing ? "Pass" : "Fail") { Value = passing };
        var result = new EvaluationResult([metric]);

        return new ScoreRecord
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = evaluatorVersion,
            Result = result,
            Source = source,
            SessionId = sessionId,
            BranchId = branchId,
            TurnIndex = turnIndex,
            AgentName = agentName,
            ModelId = modelId,
            TurnDuration = TimeSpan.FromSeconds(1),
            SamplingRate = 1.0,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Creates a ScoreRecord with a NumericMetric.
    /// </summary>
    internal static ScoreRecord MakeNumeric(
        string evaluatorName = "TestEvaluator",
        string sessionId = "session-1",
        string branchId = "main",
        int turnIndex = 0,
        string agentName = "test-agent",
        double score = 5.0,
        DateTimeOffset? createdAt = null)
    {
        var metric = new NumericMetric(evaluatorName) { Value = score };
        var result = new EvaluationResult([metric]);

        return new ScoreRecord
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = "1.0",
            Result = result,
            Source = EvaluationSource.Test,
            SessionId = sessionId,
            BranchId = branchId,
            TurnIndex = turnIndex,
            AgentName = agentName,
            TurnDuration = TimeSpan.FromSeconds(1),
            SamplingRate = 1.0,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }
}

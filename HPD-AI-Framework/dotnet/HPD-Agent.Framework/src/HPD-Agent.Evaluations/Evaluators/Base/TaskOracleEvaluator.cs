// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Evaluators;

/// <summary>
/// Base class for evaluators that verify agent output against a deterministic
/// ground-truth oracle. Subclasses execute real code — SQL, tests, API calls,
/// schema validation — and return a binary pass/fail result. No LLM required.
///
/// This is the highest-quality signal in the evaluation system. Used for scenarios
/// where a correct-answer oracle exists (e.g. SQL execution, unit tests, JSON schema validation).
/// </summary>
public abstract class TaskOracleEvaluator : HpdEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames =>
        [OracleMetricName];

    /// <summary>
    /// The metric name for this oracle. Defaults to "Oracle".
    /// Override to provide a domain-specific name (e.g. "SQL Execution", "Unit Tests").
    /// </summary>
    protected virtual string OracleMetricName => "Oracle";

    /// <summary>
    /// Run the ground-truth oracle. Return OracleResult.Pass() or OracleResult.Fail(reason).
    /// Exceptions thrown here are caught by the base class and converted to error diagnostics.
    /// </summary>
    protected abstract Task<OracleResult> RunOracleAsync(
        TurnEvaluationContext ctx,
        CancellationToken ct);

    public sealed override async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var metric = new BooleanMetric(OracleMetricName);
        var result = new EvaluationResult(metric);

        // Extract TurnEvaluationContext from additionalContext via the internal wrapper
        var turnCtx = additionalContext?
            .OfType<TurnEvaluationContextWrapper>()
            .FirstOrDefault()?.Context;

        if (turnCtx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error(
                "TurnEvaluationContext not available. TaskOracleEvaluator requires " +
                "EvaluationMiddleware to inject TurnEvaluationContextWrapper."));
            return result;
        }

        OracleResult oracle;
        try
        {
            oracle = await RunOracleAsync(turnCtx, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error(
                $"Oracle threw an exception: {ex.Message}"));
            return result;
        }

        metric.Value = oracle.Passed;
        metric.Reason = oracle.Reason;
        metric.MarkAsHpdBuiltIn();
        return result;
    }
}

/// <summary>
/// Result type returned by TaskOracleEvaluator.RunOracleAsync.
/// </summary>
public sealed record OracleResult(bool Passed, string? Reason = null)
{
    public static OracleResult Pass(string? reason = null) => new(true, reason);
    public static OracleResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// Internal EvaluationContext subclass injected by EvaluationMiddleware into every
/// additionalContext array so TaskOracleEvaluator subclasses can access the full
/// TurnEvaluationContext without breaking the MS IEvaluator signature.
/// </summary>
internal sealed class TurnEvaluationContextWrapper : EvaluationContext
{
    public TurnEvaluationContext Context { get; }

    public TurnEvaluationContextWrapper(TurnEvaluationContext ctx)
        : base("Turn Evaluation Context", $"session:{ctx.SessionId} branch:{ctx.BranchId} turn:{ctx.TurnIndex}")
    {
        Context = ctx;
    }
}

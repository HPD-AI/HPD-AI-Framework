// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Batch;

// ── IReportEvaluator ─────────────────────────────────────────────────────────

/// <summary>
/// Report-level evaluator run once after all cases complete.
/// Receives the full EvaluationReport and returns analysis results.
/// </summary>
public interface IReportEvaluator
{
    IReadOnlyList<ReportAnalysis> Evaluate(EvaluationReport report);
}

/// <summary>Result of a report-level evaluator analysis.</summary>
public sealed record ReportAnalysis(
    string AnalyzerName,
    bool Passed,
    string Message,
    IDictionary<string, object>? Details = null);

// ── EvaluatorFailure ─────────────────────────────────────────────────────────

public sealed record EvaluatorFailure(string EvaluatorName, string ErrorMessage);

// ── FailureKind ───────────────────────────────────────────────────────────────

/// <summary>
/// Distinguishes infrastructure failures from genuine agent task failures.
/// Prevents infrastructure noise from collapsing into pass rate.
/// </summary>
public enum FailureKind
{
    /// <summary>Agent threw or returned error — genuine task failure.</summary>
    TaskFailure,

    /// <summary>
    /// Infrastructure error: 429/503 exhausted after all retries.
    /// Does NOT count against pass rate but increments InfrastructureErrorRate.
    /// </summary>
    InfrastructureError,
}

/// <summary>A case that failed (agent or evaluator level).</summary>
public sealed record ReportCaseFailure(
    string? CaseName,
    FailureKind Kind,
    string ErrorMessage);

// ── ReportCase ────────────────────────────────────────────────────────────────

public sealed record ReportCase(
    string? Name,
    EvaluationResult EvaluationResult,
    IReadOnlyList<EvaluatorFailure> EvaluatorFailures,
    TimeSpan TaskDuration,
    TimeSpan EvaluatorDuration,
    TimeSpan TotalDuration);

// ── EvaluationReport ─────────────────────────────────────────────────────────

/// <summary>
/// Aggregated result from a RunEvals batch run or RetroactiveScorer.
/// </summary>
public sealed class EvaluationReport
{
    public string ExperimentName { get; }
    public IReadOnlyList<ReportCase> Cases { get; }
    public IReadOnlyList<ReportCaseFailure> Failures { get; }
    public IReadOnlyList<ReportAnalysis> Analyses { get; private set; }

    private readonly List<PolicyViolation> _policyViolations = [];

    /// <summary>
    /// Fraction of cases that failed due to infrastructure (429/503 exhausted)
    /// rather than genuine agent failures. High values indicate a flaky eval environment.
    /// Treat pass rates as unreliable when this exceeds ~2%.
    /// </summary>
    public double InfrastructureErrorRate =>
        Cases.Count == 0 ? 0.0
        : (double)Failures.Count(f => f.Kind == FailureKind.InfrastructureError) / Cases.Count;

    public bool HasPolicyViolations => _policyViolations.Count > 0;

    public EvaluationReport(
        string experimentName,
        IReadOnlyList<ReportCase> cases,
        IReadOnlyList<ReportCaseFailure>? failures = null,
        IReadOnlyList<ReportAnalysis>? analyses = null)
    {
        ExperimentName = experimentName;
        Cases = cases;
        Failures = failures ?? [];
        Analyses = analyses ?? [];
    }

    // ── Aggregate queries ─────────────────────────────────────────────────────

    /// <summary>Fraction of cases where the named metric passed (BooleanMetric = true, NumericMetric >= 0.5).</summary>
    public double PassRate(string evaluatorName)
    {
        var relevant = Cases.Where(c => c.EvaluationResult.Metrics.ContainsKey(evaluatorName)).ToList();
        if (relevant.Count == 0) return 0.0;
        int passed = relevant.Count(c => IsMetricPassing(c.EvaluationResult.Metrics[evaluatorName]));
        return (double)passed / relevant.Count;
    }

    /// <summary>Mean numeric score for the named metric across all cases.</summary>
    public double AverageScore(string evaluatorName)
    {
        var scores = Cases
            .Where(c => c.EvaluationResult.Metrics.TryGetValue(evaluatorName, out var m) &&
                        m is NumericMetric nm && nm.Value.HasValue)
            .Select(c => ((NumericMetric)c.EvaluationResult.Metrics[evaluatorName]).Value!.Value)
            .ToList();
        return scores.Count == 0 ? 0.0 : scores.Average();
    }

    public ScoreAggregate GetAggregate(string evaluatorName)
    {
        var scores = Cases
            .Where(c => c.EvaluationResult.Metrics.TryGetValue(evaluatorName, out var m) &&
                        m is NumericMetric nm && nm.Value.HasValue)
            .Select(c => ((NumericMetric)c.EvaluationResult.Metrics[evaluatorName]).Value!.Value)
            .ToList();

        if (scores.Count == 0) return new ScoreAggregate(0, 0, 0, 0, 0);

        int passing = Cases.Count(c => c.EvaluationResult.Metrics.TryGetValue(evaluatorName, out var m)
            && IsMetricPassing(m));
        return new ScoreAggregate(
            Average: scores.Average(),
            Min: scores.Min(),
            Max: scores.Max(),
            Count: scores.Count,
            PassRate: (double)passing / Cases.Count);
    }

    // ── Policy violations ─────────────────────────────────────────────────────

    internal void AddPolicyViolation(IEvaluator evaluator, double passRate)
        => _policyViolations.Add(new PolicyViolation(
            evaluator.EvaluationMetricNames.FirstOrDefault() ?? evaluator.GetType().Name,
            passRate));

    public string FormatPolicyViolations()
    {
        if (!HasPolicyViolations) return "(no policy violations)";
        return string.Join("\n", _policyViolations.Select(v =>
            $"POLICY VIOLATION: '{v.EvaluatorName}' pass rate = {v.PassRate:P1} (expected 100%)"));
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public void Print(EvaluationReport? baseline = null, PrintOptions? options = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {ExperimentName} ===");

        var metricNames = Cases
            .SelectMany(c => c.EvaluationResult.Metrics.Keys)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        foreach (var name in metricNames)
        {
            var rate = PassRate(name);
            var avg = AverageScore(name);
            var baselineRate = baseline?.PassRate(name);
            var delta = baselineRate.HasValue ? $" (Δ{rate - baselineRate.Value:+0.0%;-0.0%})" : "";
            sb.AppendLine($"  {name}: pass={rate:P1}{delta}  avg={avg:F3}");
        }

        if (Failures.Count > 0)
            sb.AppendLine($"  Failures: {Failures.Count} ({InfrastructureErrorRate:P1} infrastructure)");

        if (HasPolicyViolations)
            sb.AppendLine(FormatPolicyViolations());

        Console.Write(sb);
    }

    public string ToJson()
        => JsonSerializer.Serialize(new
        {
            experiment = ExperimentName,
            cases = Cases.Select(c => new
            {
                c.Name,
                metrics = c.EvaluationResult.Metrics.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value switch
                    {
                        NumericMetric nm => (object?)nm.Value,
                        BooleanMetric bm => bm.Value,
                        _ => null,
                    }),
                c.TaskDuration,
            }),
            infrastructure_error_rate = InfrastructureErrorRate,
        }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Writes a structured HTML report to <paramref name="outputPath"/>.
    /// Produces a self-contained HTML file with a per-metric summary table,
    /// per-case result grid, failure list, and policy violation details.
    /// Does not depend on MS HtmlReportWriter (which requires ScenarioRunResult[]
    /// with full conversation history — incompatible with EvaluationReport's data model).
    /// </summary>
    public async Task WriteHtmlAsync(string outputPath)
    {
        var metricNames = Cases
            .SelectMany(c => c.EvaluationResult.Metrics.Keys)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<title>Evaluation Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;margin:2rem;color:#222}");
        sb.AppendLine("h1{font-size:1.5rem;margin-bottom:.5rem}");
        sb.AppendLine("h2{font-size:1.1rem;margin-top:2rem;margin-bottom:.5rem;color:#444}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;margin-bottom:1.5rem}");
        sb.AppendLine("th,td{border:1px solid #ddd;padding:.4rem .7rem;text-align:left;font-size:.9rem}");
        sb.AppendLine("th{background:#f4f4f4;font-weight:600}");
        sb.AppendLine(".pass{color:#1a7a1a;font-weight:600}.fail{color:#c0392b;font-weight:600}");
        sb.AppendLine(".warn{color:#b8720a;font-weight:600}");
        sb.AppendLine(".tag{display:inline-block;padding:.15rem .4rem;border-radius:.25rem;font-size:.8rem}");
        sb.AppendLine(".tag-pass{background:#d4edda;color:#1a7a1a}.tag-fail{background:#f8d7da;color:#c0392b}");
        sb.AppendLine("</style></head><body>");

        // Header
        sb.AppendLine($"<h1>Evaluation Report: {HtmlEncode(ExperimentName)}</h1>");
        sb.AppendLine($"<p>Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC &nbsp;|&nbsp; " +
                      $"Cases: {Cases.Count} &nbsp;|&nbsp; Failures: {Failures.Count}</p>");

        if (HasPolicyViolations)
        {
            sb.AppendLine("<div style=\"background:#fdf3cd;border:1px solid #f0c040;padding:.75rem 1rem;border-radius:.3rem;margin-bottom:1rem\">");
            sb.AppendLine("<strong class=\"warn\">&#9888; Policy Violations</strong><br>");
            foreach (var line in FormatPolicyViolations().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                sb.AppendLine($"<code>{HtmlEncode(line)}</code><br>");
            sb.AppendLine("</div>");
        }

        if (InfrastructureErrorRate > 0.02)
        {
            sb.AppendLine($"<div style=\"background:#fff3cd;border:1px solid #ffc107;padding:.5rem .75rem;border-radius:.3rem;margin-bottom:1rem\">");
            sb.AppendLine($"<strong class=\"warn\">&#9888; Infrastructure error rate: {InfrastructureErrorRate:P1}</strong> — " +
                          "results may be unreliable; consider re-running affected cases.</div>");
        }

        // Per-metric summary table
        if (metricNames.Count > 0)
        {
            sb.AppendLine("<h2>Metric Summary</h2>");
            sb.AppendLine("<table><thead><tr><th>Metric</th><th>Pass Rate</th><th>Avg Score</th></tr></thead><tbody>");
            foreach (var name in metricNames)
            {
                var rate = PassRate(name);
                var avg = AverageScore(name);
                var cls = rate >= 1.0 ? "pass" : rate >= 0.8 ? "" : "fail";
                sb.AppendLine($"<tr><td>{HtmlEncode(name)}</td>" +
                              $"<td class=\"{cls}\">{rate:P1}</td>" +
                              $"<td>{avg:F3}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        // Per-case results
        if (Cases.Count > 0)
        {
            sb.AppendLine("<h2>Cases</h2>");
            sb.AppendLine("<table><thead><tr><th>Case</th>");
            foreach (var name in metricNames)
                sb.AppendLine($"<th>{HtmlEncode(name)}</th>");
            sb.AppendLine("<th>Task ms</th><th>Eval ms</th></tr></thead><tbody>");

            foreach (var c in Cases)
            {
                sb.AppendLine($"<tr><td>{HtmlEncode(c.Name ?? "(unnamed)")}</td>");
                foreach (var name in metricNames)
                {
                    if (c.EvaluationResult.Metrics.TryGetValue(name, out var metric))
                    {
                        var (display, cls) = FormatMetric(metric);
                        sb.AppendLine($"<td><span class=\"tag tag-{cls}\">{display}</span></td>");
                    }
                    else
                    {
                        sb.AppendLine("<td>—</td>");
                    }
                }
                sb.AppendLine($"<td>{c.TaskDuration.TotalMilliseconds:F0}</td>");
                sb.AppendLine($"<td>{c.EvaluatorDuration.TotalMilliseconds:F0}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        // Failures
        if (Failures.Count > 0)
        {
            sb.AppendLine("<h2>Failures</h2>");
            sb.AppendLine("<table><thead><tr><th>Case</th><th>Kind</th><th>Error</th></tr></thead><tbody>");
            foreach (var f in Failures)
            {
                sb.AppendLine($"<tr><td>{HtmlEncode(f.CaseName ?? "(unnamed)")}</td>" +
                              $"<td>{HtmlEncode(f.Kind.ToString())}</td>" +
                              $"<td>{HtmlEncode(f.ErrorMessage)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</body></html>");

        await File.WriteAllTextAsync(outputPath, sb.ToString()).ConfigureAwait(false);
    }

    private static string HtmlEncode(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static (string display, string cls) FormatMetric(EvaluationMetric metric) => metric switch
    {
        BooleanMetric bm when bm.Value == true  => ("✓", "pass"),
        BooleanMetric bm when bm.Value == false => ("✗", "fail"),
        NumericMetric nm when nm.Value.HasValue  => ($"{nm.Value.Value:F2}", nm.Value.Value >= 0.5 ? "pass" : "fail"),
        _ => ("—", "pass"),
    };

    // ── Private ───────────────────────────────────────────────────────────────

    private static bool IsMetricPassing(EvaluationMetric metric) => metric switch
    {
        BooleanMetric bm => bm.Value == true,
        NumericMetric nm => nm.Value.HasValue && nm.Value.Value >= 0.5,
        _ => false,
    };

    private sealed record PolicyViolation(string EvaluatorName, double PassRate);
}

public sealed class PrintOptions
{
    public bool ShowCaseDetails { get; init; } = false;
}

// ── Built-in report evaluators ────────────────────────────────────────────────

/// <summary>Fails the report if the named evaluator's pass rate is below the threshold.</summary>
public sealed class PassRateThresholdAnalyzer : IReportEvaluator
{
    private readonly string _evaluatorName;
    private readonly double _threshold;

    public PassRateThresholdAnalyzer(string evaluatorName, double threshold)
    {
        _evaluatorName = evaluatorName;
        _threshold = threshold;
    }

    public IReadOnlyList<ReportAnalysis> Evaluate(EvaluationReport report)
    {
        var rate = report.PassRate(_evaluatorName);
        return [new ReportAnalysis(
            GetType().Name,
            rate >= _threshold,
            $"'{_evaluatorName}' pass rate {rate:P1} {(rate >= _threshold ? ">=" : "<")} threshold {_threshold:P1}")];
    }
}

/// <summary>Fails the report if the named evaluator's average score is below the threshold.</summary>
public sealed class AverageScoreThresholdAnalyzer : IReportEvaluator
{
    private readonly string _evaluatorName;
    private readonly double _threshold;

    public AverageScoreThresholdAnalyzer(string evaluatorName, double threshold)
    {
        _evaluatorName = evaluatorName;
        _threshold = threshold;
    }

    public IReadOnlyList<ReportAnalysis> Evaluate(EvaluationReport report)
    {
        var avg = report.AverageScore(_evaluatorName);
        return [new ReportAnalysis(
            GetType().Name,
            avg >= _threshold,
            $"'{_evaluatorName}' average score {avg:F3} {(avg >= _threshold ? ">=" : "<")} threshold {_threshold:F3}")];
    }
}

/// <summary>Fails the report if the average score dropped by more than maxDelta vs baseline.</summary>
public sealed class ScoreRegressionAnalyzer : IReportEvaluator
{
    private readonly EvaluationReport _baseline;
    private readonly string _evaluatorName;
    private readonly double _maxDelta;

    public ScoreRegressionAnalyzer(EvaluationReport baseline, string evaluatorName, double maxDelta)
    {
        _baseline = baseline;
        _evaluatorName = evaluatorName;
        _maxDelta = maxDelta;
    }

    public IReadOnlyList<ReportAnalysis> Evaluate(EvaluationReport report)
    {
        var current = report.AverageScore(_evaluatorName);
        var baseline = _baseline.AverageScore(_evaluatorName);
        var delta = current - baseline;
        bool passed = delta >= -_maxDelta;
        return [new ReportAnalysis(
            GetType().Name,
            passed,
            $"'{_evaluatorName}' score delta {delta:+0.000;-0.000} (max allowed drop: {_maxDelta:F3})")];
    }
}

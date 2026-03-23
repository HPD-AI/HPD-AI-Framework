namespace HPD.RAG.Core.DTOs;

/// <summary>
/// Evaluation metrics collected from a single evaluation pipeline execution.
/// </summary>
public sealed record MragMetricsDto
{
    public required Dictionary<string, double> Scores { get; init; }
    public Dictionary<string, string>? Reasons { get; init; }
}

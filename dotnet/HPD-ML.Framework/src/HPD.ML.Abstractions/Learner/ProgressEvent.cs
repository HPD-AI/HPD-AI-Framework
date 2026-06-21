namespace HPD.ML.Abstractions;

public sealed record ProgressEvent
{
    public int? Epoch { get; init; }
    public double? MetricValue { get; init; }
    public string? MetricName { get; init; }
    public IModel? Checkpoint { get; init; }
}

using HPD.MultiAgent.Config;

namespace HPD.MultiAgent.Routing;

/// <summary>
/// Serializable condition evaluated against source-node outputs before an edge is traversed.
/// </summary>
public sealed record WorkflowCondition
{
    public required ConditionType Type { get; init; }
    public string? Field { get; init; }
    public object? Value { get; init; }
    public IReadOnlyList<WorkflowCondition>? Conditions { get; init; }
    public string? RegexOptions { get; init; }
}

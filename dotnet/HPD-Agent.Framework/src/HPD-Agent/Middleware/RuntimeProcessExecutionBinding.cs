using HPD.Environment.Contracts;

namespace HPD.Agent.Middleware;

/// <summary>
/// Runtime-selected process capability and execution target for one agent runtime.
/// Long-lived subsystems capture this value instead of inventing a target route.
/// </summary>
public sealed record RuntimeProcessExecutionBinding
{
    public required string EnvironmentId { get; init; }
    public required long EnvironmentRevision { get; init; }
    public required IProcessProvider ProcessProvider { get; init; }
    public IEnvironmentRuntime? EnvironmentRuntime { get; init; }
    public required TargetHandle<ExecutionUnit> ExecutionTarget { get; init; }
}

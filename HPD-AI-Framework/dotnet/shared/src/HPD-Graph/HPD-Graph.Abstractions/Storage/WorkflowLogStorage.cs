using HPDAgent.Graph.Abstractions.Context;

namespace HPDAgent.Graph.Abstractions.Storage;

public interface IWorkflowLogStore
{
    Task AppendAsync(WorkflowLogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowLogEntry>> ListAsync(string graphId, string executionId, CancellationToken ct = default);
    IAsyncEnumerable<WorkflowLogEntry> StreamAsync(string graphId, string executionId, CancellationToken ct = default);
}

public sealed record WorkflowLogEntry
{
    public required string GraphId { get; init; }
    public required string ExecutionId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Source { get; init; }
    public required LogLevel Level { get; init; }
    public required string Message { get; init; }
    public string? NodeId { get; init; }
    public string? Exception { get; init; }
}

using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Invocation;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Config;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using HPDAgent.Graph.Hosting.Data;

namespace HPDAgent.Graph.Hosting.Lifecycle;

public interface IWorkflowResumeRunner
{
    Task<WorkflowResumeRunnerResult> ResumeAsync(
        WorkflowResumeRunnerRequest request,
        CancellationToken ct = default);
}

public sealed record WorkflowResumeRunnerRequest
{
    public required WorkflowExecution Execution { get; init; }
    public StoredGraph? Graph { get; init; }
    public GraphCheckpoint? Checkpoint { get; init; }
    public object? ResumeValue { get; init; }
}

public sealed record WorkflowResumeRunnerResult
{
    public required ResumeSuspensionStatus Status { get; init; }
    public string? Message { get; init; }
    public bool ExecutionContinued { get; init; }

    public static WorkflowResumeRunnerResult Accepted(string? message = null, bool executionContinued = false) => new()
    {
        Status = ResumeSuspensionStatus.Accepted,
        Message = message,
        ExecutionContinued = executionContinued
    };
}

public sealed class NoOpWorkflowResumeRunner : IWorkflowResumeRunner
{
    public Task<WorkflowResumeRunnerResult> ResumeAsync(
        WorkflowResumeRunnerRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(WorkflowResumeRunnerResult.Accepted(
            "Suspension resume accepted. No workflow resume runner is configured, so execution was not continued in-process."));
    }
}

public sealed class InProcessWorkflowResumeRunner : IWorkflowResumeRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGraphCheckpointStore _checkpointStore;
    private readonly IGraphHandlerRegistry? _handlerRegistry;
    private readonly GraphConfigCompiler _compiler = new();

    public InProcessWorkflowResumeRunner(
        IServiceProvider serviceProvider,
        IGraphCheckpointStore checkpointStore,
        IGraphHandlerRegistry? handlerRegistry = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _handlerRegistry = handlerRegistry;
    }

    public async Task<WorkflowResumeRunnerResult> ResumeAsync(
        WorkflowResumeRunnerRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (request.Graph is null)
        {
            return new WorkflowResumeRunnerResult
            {
                Status = ResumeSuspensionStatus.Rejected,
                Message = "Cannot continue execution because the graph definition was not available."
            };
        }

        if (request.Checkpoint is null)
        {
            return new WorkflowResumeRunnerResult
            {
                Status = ResumeSuspensionStatus.Rejected,
                Message = "Cannot continue execution because the suspension checkpoint was not available."
            };
        }

        if (!string.Equals(request.Graph.GraphId, request.Checkpoint.GraphId, StringComparison.Ordinal) ||
            !string.Equals(request.Execution.GraphId, request.Checkpoint.GraphId, StringComparison.Ordinal) ||
            !string.Equals(request.Execution.ExecutionId, request.Checkpoint.ExecutionId, StringComparison.Ordinal))
        {
            return new WorkflowResumeRunnerResult
            {
                Status = ResumeSuspensionStatus.Rejected,
                Message = "Cannot continue execution because graph, execution, and checkpoint identity do not match."
            };
        }

        var graph = _compiler.Compile(request.Graph.Config);
        var context = new GraphContext(request.Execution.ExecutionId, graph, _serviceProvider);

        var nodeId = request.Checkpoint.Metadata?.SuspendedNodeId;
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            context.SetCurrentNode(nodeId);
            context.Channels[$"suspend_response:{nodeId}"].Set(request.ResumeValue);
            context.Channels[$"suspend_resume:{nodeId}"].Set(request.ResumeValue);
        }

        var orchestrator = new GraphOrchestrator<GraphContext>(
            _serviceProvider,
            checkpointStore: _checkpointStore,
            handlerRegistry: _handlerRegistry);

        try
        {
            await orchestrator.ResumeAsync(context, ct).ConfigureAwait(false);
            return WorkflowResumeRunnerResult.Accepted("Execution continued in-process.", executionContinued: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WorkflowResumeRunnerResult
            {
                Status = ResumeSuspensionStatus.Failed,
                Message = $"Execution resume failed: {ex.Message}"
            };
        }
    }
}

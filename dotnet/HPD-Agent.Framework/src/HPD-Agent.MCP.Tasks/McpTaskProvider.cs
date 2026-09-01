using System.Text.Json;
using HPD.Agent.Middleware;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace HPD.Agent.MCP;

/// <summary>Composes stable SDK Tasks operations into HPD's unified operation domain.</summary>
internal sealed class McpTaskProvider
{
    private readonly AgentOperationRegistry _operations;
    private readonly IMcpRecoveryReferenceProtector? _recoveryProtector;

    internal McpTaskProvider(
        AgentOperationRegistry operations,
        IMcpRecoveryReferenceProtector? recoveryProtector)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _recoveryProtector = recoveryProtector;
    }

    internal async ValueTask<McpTaskStartResult> StartAsync(
        McpClient client,
        string serverName,
        CallToolRequestParams request,
        AgentExecutionAddress address,
        FunctionInvocationSnapshot? invocation,
        string? threadExecutionId,
        FunctionExecutionContext? functionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(address);

        using var scope = McpInvocationContextScope.Push(
            serverName,
            request.Name,
            functionContext);
        var result = await client.CallToolAsTaskAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsTask)
            return McpTaskStartResult.Immediate(result.Result!);

        var created = result.TaskCreated!;
        var initialStatus = MapStatus(created.Status);
        if (initialStatus is AgentOperationProviderStatus.Completed or
            AgentOperationProviderStatus.Failed or
            AgentOperationProviderStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"MCP server '{serverName}' returned terminal status '{created.Status}' without a terminal task result.");
        }
        var operationId = Guid.NewGuid().ToString("N");
        var controller = new McpTaskController(client, created.TaskId);
        var observer = new McpTaskObserver(client, created.TaskId);
        var now = DateTimeOffset.UtcNow;
        AgentOperationRecoveryReference? recovery = null;
        if (_recoveryProtector is not null)
        {
            var serializedReference = JsonSerializer.Serialize(
                new McpTaskRecoveryReference(serverName, created.TaskId),
                McpTaskRecoveryJsonContext.Default.McpTaskRecoveryReference);
            var protectedReference = await _recoveryProtector.ProtectAsync(
                serializedReference,
                cancellationToken).ConfigureAwait(false);
            ArgumentException.ThrowIfNullOrWhiteSpace(protectedReference);
            recovery = new AgentOperationRecoveryReference("mcp-task-v1", protectedReference);
        }
        var operation = await _operations.RegisterAsync(
            new AgentOperationSnapshot
            {
                OperationId = operationId,
                ProviderOperationId = created.TaskId,
                SourceKind = AgentOperationSourceKind.McpTask,
                Name = request.Name,
                Address = address,
                OriginatingThreadExecutionId = threadExecutionId,
                Invocation = invocation,
                ProviderStatus = initialStatus,
                ObservationStatus = AgentOperationObservationStatus.Attached,
                Control = new AgentOperationControl(
                    created.TaskId,
                    AgentOperationKind.Provider,
                    AgentOperationCapabilities.Cancel |
                    AgentOperationCapabilities.Update |
                    AgentOperationCapabilities.Detach |
                    AgentOperationCapabilities.Reconcile),
                Notification = new AgentOperationNotificationPolicy
                {
                    IncludeProgress = false,
                    IncludeTerminal = true,
                    DeduplicationKey = $"mcp.task:{serverName}:{created.TaskId}"
                },
                RegisteredAt = now,
                UpdatedAt = now,
                Recovery = recovery,
                Version = 0,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["mcp.serverName"] = serverName,
                    ["mcp.taskId"] = created.TaskId
                }
            },
            controller,
            observer,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        controller.Bind(operation);
        observer.Start(operation);
        return McpTaskStartResult.Created(ToReceipt(operation.Snapshot));
    }

    private static AgentOperationReceipt ToReceipt(AgentOperationSnapshot snapshot) => new()
    {
        OperationId = snapshot.OperationId,
        ProviderOperationId = snapshot.ProviderOperationId,
        SourceKind = snapshot.SourceKind,
        Name = snapshot.Name,
        Address = snapshot.Address,
        ProviderStatus = snapshot.ProviderStatus,
        ObservationStatus = snapshot.ObservationStatus,
        Message = "Remote MCP task accepted.",
        Control = snapshot.Control,
        Metadata = snapshot.Metadata
    };

    internal static async ValueTask AttachRecoveredAsync(
        AgentOperation operation,
        McpClient client,
        string taskId,
        AgentCapabilityLease revisionLease,
        CancellationToken cancellationToken)
    {
        var controller = new McpTaskController(client, taskId);
        var observer = new McpTaskObserver(client, taskId);
        var leasedObserver = new LeasedObserver(observer, revisionLease);
        try
        {
            await operation.AttachLiveResourcesAsync(controller, leasedObserver, cancellationToken)
                .ConfigureAwait(false);
            controller.Bind(operation);
            observer.Start(operation);
        }
        catch
        {
            await leasedObserver.DisposeAsync().ConfigureAwait(false);
            await controller.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static AgentOperationProviderStatus MapStatus(McpTaskStatus status) => status switch
    {
        McpTaskStatus.Working => AgentOperationProviderStatus.Running,
        McpTaskStatus.InputRequired => AgentOperationProviderStatus.InputRequired,
        McpTaskStatus.Completed => AgentOperationProviderStatus.Completed,
        McpTaskStatus.Failed => AgentOperationProviderStatus.Failed,
        McpTaskStatus.Cancelled => AgentOperationProviderStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unsupported MCP Task status '{status}'.")
    };

    private sealed class McpTaskController(McpClient client, string taskId) : IAgentOperationController
    {
        private AgentOperation? _operation;

        internal void Bind(AgentOperation operation) =>
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));

        public async ValueTask RequestCancellationAsync(CancellationToken cancellationToken)
        {
            await client.CancelTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (_operation is { } operation)
            {
                await TransitionLatestAsync(
                    operation,
                    new AgentOperationTransition
                    {
                        ProviderStatus = AgentOperationProviderStatus.CancellationRequested,
                        ProviderDeduplicationKey = $"cancel-requested:{taskId}"
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public async ValueTask SupplyInputAsync(AgentOperationInput input, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            var responses = input.Responses.ToDictionary(
                static pair => pair.Key,
                static pair => new InputResponse { RawValue = pair.Value },
                StringComparer.Ordinal);
            await client.UpdateTaskAsync(new UpdateTaskRequestParams
            {
                TaskId = taskId,
                InputResponses = responses
            }, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class McpTaskObserver(McpClient client, string taskId) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private Task? _loop;

        internal void Start(AgentOperation operation) =>
            _loop = ObserveAsync(operation, _cancellation.Token);

        private async Task ObserveAsync(AgentOperation operation, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var state = await client.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
                var status = MapStatus(state.Status);
                AgentOperationCompletion? completion = state is CompletedTaskResult completed
                    ? new AgentOperationCompletion(Bound(completed.Result.GetRawText()))
                    : null;
                AgentOperationFailure? failure = state is FailedTaskResult failed
                    ? new AgentOperationFailure("mcp_remote_task_failed", Bound(failed.Error.ToString()))
                    : null;
                var current = operation.Snapshot;
                if (current.ProviderStatus is AgentOperationProviderStatus.Completed or
                    AgentOperationProviderStatus.Failed or
                    AgentOperationProviderStatus.Cancelled)
                    return;
                if (current.ProviderStatus == AgentOperationProviderStatus.CancellationRequested &&
                    status is AgentOperationProviderStatus.Running or AgentOperationProviderStatus.InputRequired)
                {
                    await DelayAsync(state, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                await TransitionLatestAsync(operation, new AgentOperationTransition
                    {
                        ProviderStatus = status,
                        Completion = completion,
                        Failure = failure,
                        ProviderDeduplicationKey = $"{taskId}:{state.Status}:{state.LastUpdatedAt:O}"
                    },
                    cancellationToken).ConfigureAwait(false);
                if (status is AgentOperationProviderStatus.Completed or
                    AgentOperationProviderStatus.Failed or
                    AgentOperationProviderStatus.Cancelled)
                    return;
                await DelayAsync(state, cancellationToken).ConfigureAwait(false);
            }
        }

        private static Task DelayAsync(GetTaskResult state, CancellationToken cancellationToken)
        {
            var delay = state.PollIntervalMs is > 0
                ? TimeSpan.FromMilliseconds(state.PollIntervalMs.Value)
                : TimeSpan.FromSeconds(1);
            return Task.Delay(delay, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
            if (_loop is not null)
            {
                try { await _loop.ConfigureAwait(false); }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
            }
            _cancellation.Dispose();
        }
    }

    private sealed class LeasedObserver(
        IAsyncDisposable observer,
        AgentCapabilityLease revisionLease) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await observer.DisposeAsync().ConfigureAwait(false);
            await revisionLease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string Bound(string? value)
    {
        value ??= string.Empty;
        const int maximum = 4096;
        return value.Length <= maximum ? value : value[..maximum];
    }

    private static async ValueTask<AgentOperationTransitionResult> TransitionLatestAsync(
        AgentOperation operation,
        AgentOperationTransition transition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var version = operation.Snapshot.Version;
            try
            {
                return await operation.TransitionAsync(
                    transition,
                    version,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (AgentOperationVersionConflictException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}

/// <summary>Represents either an immediate tool result or a registered remote operation.</summary>
internal sealed record McpTaskStartResult(CallToolResult? Result, AgentOperationReceipt? Receipt)
{
    internal static McpTaskStartResult Immediate(CallToolResult result) => new(result, null);
    internal static McpTaskStartResult Created(AgentOperationReceipt receipt) => new(null, receipt);
}

/// <summary>Contains the protected identity needed to recover remote task observation.</summary>
internal sealed record McpTaskRecoveryReference(string ServerName, string TaskId);

[System.Text.Json.Serialization.JsonSerializable(typeof(McpTaskRecoveryReference))]
internal partial class McpTaskRecoveryJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

namespace HPD.Agent;

/// <summary>Schedules HPD-owned work into the authoritative operation registry.</summary>
internal static class AgentLocalOperationScheduler
{
    internal static async ValueTask<AgentOperationReceipt> StartAsync(
        AgentOperationRegistry registry,
        AgentOperationSourceKind sourceKind,
        string name,
        AgentExecutionAddress address,
        string? threadExecutionId,
        Middleware.FunctionInvocationSnapshot? invocation,
        IReadOnlyDictionary<string, string>? metadata,
        AgentOperationNotificationPolicy notification,
        Func<string, CancellationToken, ValueTask<AgentOperationCompletion>> work,
        CancellationToken runtimeCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(work);

        var operationId = Guid.NewGuid().ToString("N");
        var controller = new LocalController(runtimeCancellationToken);
        var observer = new LocalObserver(controller, work);
        var now = DateTimeOffset.UtcNow;
        var operation = await registry.RegisterAsync(new AgentOperationSnapshot
        {
            OperationId = operationId,
            SourceKind = sourceKind,
            Name = name,
            Address = address,
            OriginatingThreadExecutionId = threadExecutionId,
            Invocation = invocation,
            ProviderStatus = AgentOperationProviderStatus.Accepted,
            ObservationStatus = AgentOperationObservationStatus.Attached,
            Control = new AgentOperationControl(
                operationId,
                AgentOperationKind.Task,
                AgentOperationCapabilities.Cancel),
            Notification = notification with
            {
                DeduplicationKey = notification.DeduplicationKey ?? $"local:{operationId}"
            },
            RegisteredAt = now,
            UpdatedAt = now,
            Version = 0,
            Metadata = metadata
        }, controller, observer).ConfigureAwait(false);
        controller.Bind(operation);
        observer.Start(operation);
        return ToReceipt(operation.Snapshot);
    }

    private static AgentOperationReceipt ToReceipt(AgentOperationSnapshot snapshot) => new()
    {
        OperationId = snapshot.OperationId,
        SourceKind = snapshot.SourceKind,
        Name = snapshot.Name,
        Address = snapshot.Address,
        ProviderStatus = snapshot.ProviderStatus,
        ObservationStatus = snapshot.ObservationStatus,
        Message = $"Started {snapshot.Name} in the background.",
        Control = snapshot.Control,
        Metadata = snapshot.Metadata
    };

    private sealed class LocalController(CancellationToken runtimeCancellationToken) : IAgentOperationController
    {
        private readonly CancellationTokenSource _cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellationToken);
        private AgentOperation? _operation;
        internal CancellationToken Token => _cancellation.Token;
        internal void Bind(AgentOperation operation) => _operation = operation;

        public async ValueTask RequestCancellationAsync(CancellationToken cancellationToken)
        {
            if (_operation is { } operation)
                await TransitionLatestAsync(operation, new AgentOperationTransition
                {
                    ProviderStatus = AgentOperationProviderStatus.CancellationRequested,
                    ProviderDeduplicationKey = $"cancel-requested:{operation.Snapshot.OperationId}"
                }, cancellationToken).ConfigureAwait(false);
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }

        public ValueTask SupplyInputAsync(AgentOperationInput input, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Local operations do not accept input updates."));

        public ValueTask DisposeAsync()
        {
            _cancellation.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LocalObserver(
        LocalController controller,
        Func<string, CancellationToken, ValueTask<AgentOperationCompletion>> work) : IAsyncDisposable
    {
        private Task? _task;
        internal void Start(AgentOperation operation) => _task = RunAsync(operation);

        private async Task RunAsync(AgentOperation operation)
        {
            try
            {
                await TransitionLatestAsync(operation, new AgentOperationTransition
                {
                    ProviderStatus = AgentOperationProviderStatus.Running,
                    ProviderDeduplicationKey = $"running:{operation.Snapshot.OperationId}"
                }, controller.Token).ConfigureAwait(false);
                var completion = await work(operation.Snapshot.OperationId, controller.Token).ConfigureAwait(false);
                await TransitionLatestAsync(operation, new AgentOperationTransition
                {
                    ProviderStatus = AgentOperationProviderStatus.Completed,
                    Completion = completion,
                    ProviderDeduplicationKey = $"completed:{operation.Snapshot.OperationId}"
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (controller.Token.IsCancellationRequested)
            {
                await TransitionLatestAsync(operation, new AgentOperationTransition
                {
                    ProviderStatus = AgentOperationProviderStatus.Cancelled,
                    ProviderDeduplicationKey = $"cancelled:{operation.Snapshot.OperationId}"
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await TransitionLatestAsync(operation, new AgentOperationTransition
                {
                    ProviderStatus = AgentOperationProviderStatus.Failed,
                    Failure = new AgentOperationFailure("local_operation_failed", Bound(ex.Message)),
                    ProviderDeduplicationKey = $"failed:{operation.Snapshot.OperationId}"
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_task is not null)
                await _task.ConfigureAwait(false);
        }
    }

    private static async ValueTask TransitionLatestAsync(
        AgentOperation operation,
        AgentOperationTransition transition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await operation.TransitionAsync(
                    transition,
                    operation.Snapshot.Version,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (AgentOperationVersionConflictException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static string Bound(string value) => value.Length <= 4096 ? value : value[..4096];
}

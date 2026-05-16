namespace HPD.Agent.Tests.Infrastructure;
using HPD.Agent;
/// <summary>
/// Mock permission handler that automatically responds to permission requests during tests.
/// Allows programmatic control over approval/denial decisions.
/// </summary>
public sealed class MockPermissionHandler : IDisposable
{
    private readonly Agent _agent;
    private readonly Task _handlerTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly List<PermissionRequestEvent> _capturedRequests = new();
    private readonly List<AgentEvent> _capturedEvents = new();
    private readonly Queue<PermissionResponse> _queuedResponses = new();
    private readonly HashSet<string> _handledPermissionRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _handledContinuationRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _capturedMiddlewareEvents = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private bool _autoApprove = false;
    private bool _autoDeny = false;
    private bool _autoApproveContinuation = true; // Default: auto-approve continuations
    private bool _autoDenyContinuation = false;

    /// <summary>
    /// Permission response configuration.
    /// </summary>
    public record PermissionResponse(
        bool Approved,
        string? DenialReason = null,
        PermissionChoice Choice = PermissionChoice.Ask);

        internal MockPermissionHandler(Agent agent, IAsyncEnumerable<AgentEvent> eventStream)
    {
        _agent = agent;
        _subscriptions.Add(agent.EventCoordinator.Subscribe<PermissionRequestEvent>(
            evt => new ValueTask(HandleEventAsync(evt))));
        _subscriptions.Add(agent.EventCoordinator.Subscribe<PermissionApprovedEvent>(
            evt => new ValueTask(CaptureMiddlewareEventAsync(evt))));
        _subscriptions.Add(agent.EventCoordinator.Subscribe<PermissionDeniedEvent>(
            evt => new ValueTask(CaptureMiddlewareEventAsync(evt))));
        _subscriptions.Add(agent.EventCoordinator.Subscribe<ContinuationRequestEvent>(
            evt => new ValueTask(HandleEventAsync(evt))));
        _handlerTask = Task.Run(async () => await HandleEventsAsync(eventStream));
    }

    /// <summary>
    /// Gets all permission requests that were captured.
    /// </summary>
    public IReadOnlyList<PermissionRequestEvent> CapturedRequests
    {
        get
        {
            lock (_lock)
            {
                return _capturedRequests.ToList();
            }
        }
    }

    /// <summary>
    /// Gets all events that were captured.
    /// </summary>
    public IReadOnlyList<AgentEvent> CapturedEvents
    {
        get
        {
            lock (_lock)
            {
                return _capturedEvents.ToList();
            }
        }
    }

    /// <summary>
    /// Configures handler to automatically approve all permission requests.
    /// </summary>
    public MockPermissionHandler AutoApproveAll()
    {
        lock (_lock)
        {
            _autoApprove = true;
            _autoDeny = false;
        }
        return this;
    }

    /// <summary>
    /// Configures handler to automatically deny all permission requests.
    /// </summary>
    public MockPermissionHandler AutoDenyAll(string reason = "Denied by test")
    {
        lock (_lock)
        {
            _autoApprove = false;
            _autoDeny = true;
        }
        return this;
    }

    /// <summary>
    /// Configures handler to automatically deny continuation requests.
    /// This causes the agent to terminate when the iteration limit is reached.
    /// </summary>
    public MockPermissionHandler AutoDenyContinuation()
    {
        lock (_lock)
        {
            _autoApproveContinuation = false;
            _autoDenyContinuation = true;
        }
        return this;
    }

    /// <summary>
    /// Queues a specific response for the next permission request.
    /// </summary>
    public MockPermissionHandler EnqueueResponse(bool approved, string? denialReason = null, PermissionChoice choice = PermissionChoice.Ask)
    {
        lock (_lock)
        {
            _queuedResponses.Enqueue(new PermissionResponse(approved, denialReason, choice));
        }
        return this;
    }

    /// <summary>
    /// Queues multiple responses.
    /// </summary>
    public MockPermissionHandler EnqueueResponses(params PermissionResponse[] responses)
    {
        lock (_lock)
        {
            foreach (var response in responses)
            {
                _queuedResponses.Enqueue(response);
            }
        }
        return this;
    }

    private async Task HandleEventsAsync(IAsyncEnumerable<AgentEvent> eventStream)
    {
        try
        {
            await foreach (var evt in eventStream.WithCancellation(_cts.Token))
            {
                await HandleEventAsync(evt);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposed
        }
    }

    private Task CaptureMiddlewareEventAsync(AgentEvent evt)
    {
        lock (_lock)
        {
            if (_capturedMiddlewareEvents.Add(GetMiddlewareEventKey(evt)))
                _capturedEvents.Add(evt);
        }

        return Task.CompletedTask;
    }

    private async Task HandleEventAsync(AgentEvent evt)
    {
        // Capture ALL events from the run stream. Middleware-emitted permission lifecycle
        // events arrive through EventCoordinator subscriptions because the runtime can be
        // waiting inside the middleware hook when they are emitted.
        if (evt is not PermissionApprovedEvent and not PermissionDeniedEvent)
        {
            lock (_lock)
            {
                _capturedEvents.Add(evt);
            }
        }
        else
        {
            await CaptureMiddlewareEventAsync(evt);
        }

        if (evt is PermissionRequestEvent permissionRequest)
        {
            PermissionResponse response;
            lock (_lock)
            {
                if (!_handledPermissionRequests.Add(permissionRequest.PermissionId))
                    return;

                _capturedRequests.Add(permissionRequest);

                if (_queuedResponses.Count > 0)
                {
                    response = _queuedResponses.Dequeue();
                }
                else if (_autoApprove)
                {
                    response = new PermissionResponse(true);
                }
                else if (_autoDeny)
                {
                    response = new PermissionResponse(false, "Denied by test");
                }
                else
                {
                    response = new PermissionResponse(true);
                }
            }

            await _agent.RespondAsync(new PermissionResponseEvent(
                permissionRequest.PermissionId,
                "MockPermissionHandler",
                response.Approved,
                response.DenialReason,
                response.Choice));
        }
        else if (evt is ContinuationRequestEvent continuationRequest)
        {
            bool approved;
            lock (_lock)
            {
                if (!_handledContinuationRequests.Add(continuationRequest.ContinuationId))
                    return;

                approved = _autoApproveContinuation && !_autoDenyContinuation;
            }

            await _agent.RespondAsync(new ContinuationResponseEvent(
                continuationRequest.ContinuationId,
                "MockPermissionHandler",
                approved));
        }
    }

    private static string GetMiddlewareEventKey(AgentEvent evt) =>
        evt switch
        {
            PermissionApprovedEvent approved => $"{nameof(PermissionApprovedEvent)}:{approved.PermissionId}",
            PermissionDeniedEvent denied => $"{nameof(PermissionDeniedEvent)}:{denied.PermissionId}",
            _ => $"{evt.GetType().FullName}:{evt.GetHashCode()}"
        };

    /// <summary>
    /// Waits for a specific number of permission requests to be captured.
    /// </summary>
    public async Task<bool> WaitForRequestsAsync(int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                if (_capturedRequests.Count >= count)
                    return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    /// <summary>
    /// Waits for the event stream to complete (agent loop finished).
    /// </summary>
    public async Task WaitForCompletionAsync(TimeSpan timeout)
    {
        var completed = await Task.WhenAny(_handlerTask, Task.Delay(timeout));
        if (completed != _handlerTask)
        {
            int requestCount;
            int eventCount;
            lock (_lock)
            {
                requestCount = _capturedRequests.Count;
                eventCount = _capturedEvents.Count;
            }
            string eventTypes;
            lock (_lock)
            {
                eventTypes = string.Join(", ", _capturedEvents.Select(e => e.GetType().Name).TakeLast(12));
            }

            throw new TimeoutException(
                $"MockPermissionHandler did not complete within {timeout.TotalSeconds} seconds. " +
                $"Captured {requestCount} permission request(s) and {eventCount} event(s). " +
                $"Recent events: {eventTypes}.");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        try
        {
            _handlerTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected - task was cancelled
        }
        _cts.Dispose();
    }
}

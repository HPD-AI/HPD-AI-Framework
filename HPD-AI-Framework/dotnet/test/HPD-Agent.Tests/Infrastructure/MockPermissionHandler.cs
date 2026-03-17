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
    private readonly List<PermissionRequestEvent> _capturedRequests = new();
    private readonly List<AgentEvent> _capturedEvents = new();
    private readonly Queue<PermissionResponse> _queuedResponses = new();
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
                // Capture ALL events
                lock (_lock)
                {
                    _capturedEvents.Add(evt);
                }

                if (evt is PermissionRequestEvent permissionRequest)
                {
                    // Capture the request
                    lock (_lock)
                    {
                        _capturedRequests.Add(permissionRequest);
                    }

                    // Determine response
                    PermissionResponse response;
                    lock (_lock)
                    {
                        if (_queuedResponses.Count > 0)
                        {
                            // Use queued response
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
                            // Default: approve
                            response = new PermissionResponse(true);
                        }
                    }

                    // Send response back to agent
                    _agent.SendMiddlewareResponse(
                        permissionRequest.PermissionId,
                        new PermissionResponseEvent(
                            permissionRequest.PermissionId,
                            "MockPermissionHandler",
                            response.Approved,
                            response.DenialReason,
                            response.Choice));
                }
                else if (evt is ContinuationRequestEvent continuationRequest)
                {
                    // Respond to continuation requests based on configuration
                    bool approved;
                    lock (_lock)
                    {
                        approved = _autoApproveContinuation && !_autoDenyContinuation;
                    }

                    _agent.SendMiddlewareResponse(
                        continuationRequest.ContinuationId,
                        new ContinuationResponseEvent(
                            continuationRequest.ContinuationId,
                            "MockPermissionHandler",
                            approved));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposed
        }
    }

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
            throw new TimeoutException($"MockPermissionHandler did not complete within {timeout.TotalSeconds} seconds");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
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

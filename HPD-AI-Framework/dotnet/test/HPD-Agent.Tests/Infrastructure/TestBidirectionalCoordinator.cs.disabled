using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using HPD.Agent;


namespace HPD.Agent.Tests.Infrastructure;

/// <summary>
/// Test double for BidirectionalEventCoordinator that captures all events
/// and allows programmatic responses for testing bidirectional communication.
/// </summary>
public sealed class TestBidirectionalCoordinator
{
    private readonly Channel<AgentEvent> _eventChannel;
    private readonly Dictionary<string, TaskCompletionSource<AgentEvent>> _pendingResponses = new();
    private readonly List<AgentEvent> _capturedEvents = new();
    private readonly object _lock = new();

    public TestBidirectionalCoordinator()
    {
        _eventChannel = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Gets all events that have been captured.
    /// Thread-safe snapshot of events.
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
    /// Attempts to read an event synchronously.
    /// </summary>
    /// <param name="event">The event if one was available</param>
    /// <returns>True if an event was read, false otherwise</returns>
    public bool TryRead([NotNullWhen(true)] out AgentEvent? @event)
    {
        return _eventChannel.Reader.TryRead(out @event);
    }

    /// <summary>
    /// Emits an event to the channel.
    /// </summary>
    public void Emit(AgentEvent evt)
    {
        _eventChannel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Captures an event for test verification.
    /// Automatically captures events written to the channel.
    /// </summary>
    public void CaptureEvent(AgentEvent evt)
    {
        lock (_lock)
        {
            _capturedEvents.Add(evt);
        }

        // Also write to channel so agent can read it
        _eventChannel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Enqueues a Middleware event to be read by the agent.
    /// Useful for simulating permission responses, progress updates, etc.
    /// </summary>
    public void EnqueueEvent(AgentEvent evt)
    {
        _eventChannel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Sends a response to a waiting Middleware request.
    /// Simulates user responding to permission prompt, etc.
    /// </summary>
    public void SendResponse(string requestId, AgentEvent response)
    {
        lock (_lock)
        {
            if (_pendingResponses.TryGetValue(requestId, out var tcs))
            {
                tcs.TrySetResult(response);
                _pendingResponses.Remove(requestId);
            }
            else
            {
                throw new InvalidOperationException(
                    $"No pending request found for requestId: {requestId}");
            }
        }
    }

    /// <summary>
    /// Waits for a response from external source (simulates Middleware waiting for user input).
    /// </summary>
    public async Task<T> WaitForResponseAsync<T>(
        string requestId,
        TimeSpan timeout,
        CancellationToken cancellationToken) where T : AgentEvent
    {
        var tcs = new TaskCompletionSource<AgentEvent>();

        lock (_lock)
        {
            _pendingResponses[requestId] = tcs;
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            var result = await tcs.Task.WaitAsync(linkedCts.Token);

            if (result is not T typedResult)
            {
                throw new InvalidOperationException(
                    $"Response type mismatch: expected {typeof(T).Name}, got {result.GetType().Name}");
            }

            return typedResult;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            lock (_lock)
            {
                _pendingResponses.Remove(requestId);
            }
            throw new TimeoutException(
                $"Timeout waiting for response to request {requestId} after {timeout.TotalSeconds}s");
        }
    }

    /// <summary>
    /// Gets all events of a specific type.
    /// </summary>
    public IReadOnlyList<T> GetEvents<T>() where T : AgentEvent
    {
        lock (_lock)
        {
            return _capturedEvents.OfType<T>().ToList();
        }
    }

    /// <summary>
    /// Gets the event sequence as type names.
    /// Useful for asserting event order.
    /// </summary>
    public IReadOnlyList<string> GetEventTypeSequence()
    {
        lock (_lock)
        {
            return _capturedEvents.Select(e => e.GetType().Name).ToList();
        }
    }

    /// <summary>
    /// Checks if an event of type T exists.
    /// </summary>
    public bool ContainsEvent<T>() where T : AgentEvent
    {
        lock (_lock)
        {
            return _capturedEvents.Any(e => e is T);
        }
    }

    /// <summary>
    /// Waits for a specific event type to appear in the captured events.
    /// Useful for async event verification.
    /// </summary>
    public async Task<T> WaitForEventAsync<T>(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) where T : AgentEvent
    {
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                var evt = _capturedEvents.OfType<T>().FirstOrDefault();
                if (evt != null)
                    return evt;
            }

            await Task.Delay(10, cancellationToken);
        }

        throw new TimeoutException(
            $"Event of type {typeof(T).Name} did not appear within {timeout.TotalSeconds}s");
    }

    /// <summary>
    /// Clears all captured events and pending responses.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _capturedEvents.Clear();
            _pendingResponses.Clear();
        }

        // Drain the channel
        while (_eventChannel.Reader.TryRead(out _))
        {
            // Discard
        }
    }

    /// <summary>
    /// Gets a count of captured events.
    /// </summary>
    public int EventCount
    {
        get
        {
            lock (_lock)
            {
                return _capturedEvents.Count;
            }
        }
    }
}

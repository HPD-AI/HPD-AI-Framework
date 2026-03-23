using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace HPD.Events.Core;

/// <summary>
/// Event coordinator implementation with priority-based routing and hierarchical bubbling.
/// Thread-safe, non-generic design works with any Event subclass without type conversions.
/// </summary>
public sealed class EventCoordinator : IEventCoordinator, IDisposable
{
    //
    // CHANNELS
    //

    /// <summary>
    /// Priority channel for Immediate and Control events.
    /// Bounded to prevent memory issues, but should rarely fill.
    /// </summary>
    private readonly Channel<Event> _priorityChannel;

    /// <summary>
    /// Standard channel for Normal and Background events.
    /// Unbounded to prevent blocking during high-throughput streaming.
    /// </summary>
    private readonly Channel<Event> _standardChannel;

    /// <summary>
    /// Upstream channel for events flowing back through the pipeline.
    /// Used for interruption propagation.
    /// </summary>
    private readonly Channel<Event> _upstreamChannel;

    //
    // STATE
    //

    /// <summary>
    /// Monotonically increasing sequence counter for event ordering.
    /// </summary>
    private long _sequenceCounter;

    /// <summary>
    /// Response coordination for bidirectional patterns.
    /// Maps requestId -> (TaskCompletionSource, CancellationTokenSource)
    /// Thread-safe: ConcurrentDictionary handles concurrent access.
    /// </summary>
    private readonly ConcurrentDictionary<string, (TaskCompletionSource<Event>, CancellationTokenSource)>
        _responseWaiters = new();

    /// <summary>
    /// Parent coordinator for event bubbling in nested scenarios.
    /// Events emitted to this coordinator will also bubble to the parent.
    /// </summary>
    private IEventCoordinator? _parentCoordinator;

    /// <summary>
    /// Stream registry for managing interruptible streams.
    /// </summary>
    private readonly StreamRegistry _streamRegistry = new();

    /// <summary>
    /// Optional callback for enriching events before emission.
    /// Domain-specific coordinators can use this to attach context automatically.
    /// </summary>
    private readonly Func<Event, Event>? _eventEnricher;

    /// <summary>
    /// Optional callback for filtering events before emission.
    /// Return false to skip emitting the event.
    /// </summary>
    private readonly Func<Event, bool>? _eventFilter;

    /// <summary>
    /// Disposed flag for cleanup.
    /// </summary>
    private bool _disposed;

    //
    // CONSTRUCTION
    //

    /// <summary>
    /// Creates a new event coordinator with priority-based routing.
    /// </summary>
    /// <param name="eventEnricher">Optional callback to enrich events before emission (e.g., attach context)</param>
    /// <param name="eventFilter">Optional callback to filter events (return false to skip emission)</param>
    public EventCoordinator(
        Func<Event, Event>? eventEnricher = null,
        Func<Event, bool>? eventFilter = null)
    {
        _eventEnricher = eventEnricher;
        _eventFilter = eventFilter;

        // Priority channel: bounded, for Immediate/Control events
        _priorityChannel = Channel.CreateBounded<Event>(new BoundedChannelOptions(64)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        // Standard channel: unbounded, for Normal/Background events
        _standardChannel = Channel.CreateUnbounded<Event>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        // Upstream channel: bounded, for interruptions flowing back
        _upstreamChannel = Channel.CreateBounded<Event>(new BoundedChannelOptions(64)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    //
    // PUBLIC API
    //

    /// <inheritdoc />
    public IStreamRegistry Streams => _streamRegistry;

    /// <inheritdoc />
    public void Emit(Event evt)
    {
        if (evt == null)
            throw new ArgumentNullException(nameof(evt));

        if (_disposed)
            throw new ObjectDisposedException(nameof(EventCoordinator));

        // Apply filter (if configured)
        if (_eventFilter != null && !_eventFilter(evt))
            return; // Skip emission

        // Assign sequence number
        evt.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);

        // Apply enrichment (if configured)
        var enriched = _eventEnricher?.Invoke(evt) ?? evt;

        // Check if event should be dropped due to stream interruption
        if (enriched.StreamId != null && enriched.CanInterrupt && !(enriched is EventDroppedEvent))
        {
            var streamHandle = _streamRegistry.Get(enriched.StreamId);
            if (streamHandle is StreamHandle handle && handle.IsInterrupted)
            {
                // Track dropped count
                handle.IncrementDroppedCount();

                // Emit universal EventDroppedEvent (without StreamId to avoid recursion)
                var droppedEvent = new EventDroppedEvent(
                    enriched.StreamId,
                    enriched.GetType().Name,
                    enriched.SequenceNumber
                );
                EmitInternal(droppedEvent);

                // Don't emit the original event
                return;
            }
        }

        // Track emitted count for interruptible events that pass through
        if (enriched.StreamId != null && enriched.CanInterrupt && !(enriched is EventDroppedEvent))
        {
            var streamHandle = _streamRegistry.Get(enriched.StreamId);
            if (streamHandle is StreamHandle handle)
            {
                handle.IncrementEmittedCount();
            }
        }

        // Route to appropriate channel
        EmitInternal(enriched);

        // Bubble to parent (if set)
        _parentCoordinator?.Emit(enriched);
    }

    /// <inheritdoc />
    public void EmitUpstream(Event evt)
    {
        if (evt == null)
            throw new ArgumentNullException(nameof(evt));

        if (_disposed)
            throw new ObjectDisposedException(nameof(EventCoordinator));

        // Assign sequence number
        evt.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);

        // Apply enrichment (if configured)
        var enriched = _eventEnricher?.Invoke(evt) ?? evt;

        if (!_upstreamChannel.Writer.TryWrite(enriched))
        {
            throw new InvalidOperationException(
                "Upstream event channel full. This indicates a processing bottleneck.");
        }

        // Bubble upstream to parent
        _parentCoordinator?.EmitUpstream(enriched);
    }

    /// <inheritdoc />
    public bool TryRead([NotNullWhen(true)] out Event? evt)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EventCoordinator));

        // Priority order: Upstream > Priority (Immediate/Control) > Standard (Normal/Background)

        // 1. Try upstream channel first
        if (_upstreamChannel.Reader.TryRead(out var upstreamEvent))
        {
            evt = upstreamEvent;
            return true;
        }

        // 2. Try priority channel (Immediate/Control)
        if (_priorityChannel.Reader.TryRead(out var priorityEvent))
        {
            evt = priorityEvent;
            return true;
        }

        // 3. Try standard channel (Normal/Background)
        if (_standardChannel.Reader.TryRead(out var standardEvent))
        {
            evt = standardEvent;
            return true;
        }

        evt = null;
        return false;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Event> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EventCoordinator));

        while (!ct.IsCancellationRequested)
        {
            // 1. ALWAYS drain priority channel first (Immediate/Control)
            while (_priorityChannel.Reader.TryRead(out var priorityEvent))
            {
                yield return priorityEvent;
            }

            // 2. Check upstream channel (interruptions flowing back)
            while (_upstreamChannel.Reader.TryRead(out var upstreamEvent))
            {
                yield return upstreamEvent;
            }

            // 3. Then read from standard channel (Normal/Background)
            if (_standardChannel.Reader.TryRead(out var standardEvent))
            {
                yield return standardEvent;
            }
            else
            {
                // Wait for any channel to have data
                try
                {
                    var priorityTask = _priorityChannel.Reader.WaitToReadAsync(ct).AsTask();
                    var upstreamTask = _upstreamChannel.Reader.WaitToReadAsync(ct).AsTask();
                    var standardTask = _standardChannel.Reader.WaitToReadAsync(ct).AsTask();

                    await Task.WhenAny(priorityTask, upstreamTask, standardTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break; // Graceful exit on cancellation
                }
            }
        }
    }

    /// <inheritdoc />
    public void SetParent(IEventCoordinator parent)
    {
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));

        if (_disposed)
            throw new ObjectDisposedException(nameof(EventCoordinator));

        // Check for self-reference (simplest cycle)
        if (parent == this)
            throw new InvalidOperationException(
                "Cannot set coordinator as its own parent. This would create an infinite loop during event emission.");

        // Check for cycles by traversing the parent chain
        var current = parent;
        while (current != null)
        {
            if (current == this)
                throw new InvalidOperationException(
                    "Cannot set parent: this would create a cycle in the coordinator hierarchy, " +
                    "causing infinite loops during event emission.");

            // Get the parent of the current coordinator (only works for EventCoordinator instances)
            if (current is EventCoordinator coordinator)
            {
                current = coordinator._parentCoordinator;
            }
            else
            {
                // Can't traverse further if it's not an EventCoordinator
                break;
            }
        }

        _parentCoordinator = parent;
    }

    /// <inheritdoc />
    public async Task<TResponse> WaitForResponseAsync<TResponse>(
        string requestId,
        TimeSpan timeout,
        CancellationToken ct = default) where TResponse : Event
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request ID cannot be null or whitespace", nameof(requestId));

        if (_disposed)
            throw new ObjectDisposedException(nameof(EventCoordinator));

        var tcs = new TaskCompletionSource<Event>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (!_responseWaiters.TryAdd(requestId, (tcs, cts)))
        {
            throw new InvalidOperationException($"Duplicate request ID: {requestId}");
        }

        try
        {
            // Set timeout
            cts.CancelAfter(timeout);

            // Register cancellation handler
            using var registration = cts.Token.Register(() =>
            {
                _responseWaiters.TryRemove(requestId, out _);
                tcs.TrySetCanceled(cts.Token);
            });

            // Wait for response
            var response = await tcs.Task.ConfigureAwait(false);

            if (response is not TResponse typedResponse)
            {
                throw new InvalidOperationException(
                    $"Response type mismatch. Expected {typeof(TResponse).Name}, got {response.GetType().Name}");
            }

            return typedResponse;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout occurred (not user cancellation)
            throw new TimeoutException($"No response received for request ID '{requestId}' within {timeout.TotalSeconds:F1}s");
        }
        finally
        {
            _responseWaiters.TryRemove(requestId, out _);
            cts.Dispose();
        }
    }

    /// <inheritdoc />
    public void SendResponse(string requestId, Event response)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request ID cannot be null or whitespace", nameof(requestId));

        if (response == null)
            throw new ArgumentNullException(nameof(response));

        if (_disposed)
            throw new ObjectDisposedException(nameof(EventCoordinator));

        if (_responseWaiters.TryRemove(requestId, out var waiter))
        {
            waiter.Item1.TrySetResult(response);
            waiter.Item2.Dispose();
        }
    }

    //
    // INTERNAL HELPERS
    //

    /// <summary>
    /// Emits event to appropriate channel based on priority.
    /// </summary>
    private void EmitInternal(Event evt)
    {
        // Route based on priority
        var channel = evt.Priority switch
        {
            EventPriority.Immediate => _priorityChannel,
            EventPriority.Control => _priorityChannel,
            EventPriority.Upstream => _upstreamChannel,
            EventPriority.Normal => _standardChannel,
            EventPriority.Background => _standardChannel,
            _ => _standardChannel
        };

        if (!channel.Writer.TryWrite(evt))
        {
            throw new InvalidOperationException(
                $"Failed to write event to {evt.Priority} channel. This indicates a processing bottleneck.");
        }
    }

    //
    // DISPOSAL
    //

    /// <summary>
    /// Dispose coordinator and complete all channels.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Complete all channels to signal no more events
        _priorityChannel.Writer.Complete();
        _standardChannel.Writer.Complete();
        _upstreamChannel.Writer.Complete();

        // Cancel all pending response waiters
        foreach (var (requestId, (tcs, cts)) in _responseWaiters)
        {
            tcs.TrySetCanceled();
            cts.Dispose();
        }

        _responseWaiters.Clear();
    }
}

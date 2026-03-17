// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace HPD.Agent;

/// <summary>
/// Wraps a single <see cref="IAgentEventObserver"/> with a dedicated unbounded channel
/// and a background loop that processes events strictly in FIFO order, one at a time.
///
/// This eliminates the race conditions caused by the previous <c>Task.Run</c> fan-out
/// in <c>NotifyObservers</c>, where two consecutive tasks for the same observer could
/// be scheduled out of order by the thread pool.
/// </summary>
internal sealed class ObserverDispatcher : IDisposable
{
    private readonly IAgentEventObserver _observer;
    private readonly ObserverHealthTracker? _healthTracker;
    private readonly ILogger? _errorLogger;
    private readonly bool _emitObservabilityEvents;

    private readonly Channel<AgentEvent> _channel = Channel.CreateUnbounded<AgentEvent>(
        new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

    private readonly Task _loop;

    public ObserverDispatcher(
        IAgentEventObserver observer,
        ObserverHealthTracker? healthTracker,
        ILogger? errorLogger,
        bool emitObservabilityEvents)
    {
        _observer = observer;
        _healthTracker = healthTracker;
        _errorLogger = errorLogger;
        _emitObservabilityEvents = emitObservabilityEvents;

        _loop = Task.Run(RunAsync);
    }

    /// <summary>
    /// Enqueues an event for sequential processing by this observer.
    /// Returns immediately; the background loop processes the event asynchronously.
    /// </summary>
    public void Enqueue(AgentEvent evt)
    {
        // Skip observability events when not enabled
        if (evt is IObservabilityEvent && !_emitObservabilityEvents)
            return;

        // Skip if observer doesn't want this event
        if (!_observer.ShouldProcess(evt))
            return;

        // Skip if circuit breaker is open
        if (_healthTracker != null && !_healthTracker.ShouldProcess(_observer))
            return;

        _channel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Signals no more events will be enqueued and waits for the background loop
    /// to finish processing all queued events.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _channel.Writer.TryComplete();
        await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        // Do not await _loop here — fire-and-forget cleanup is acceptable on Dispose.
    }

    private async Task RunAsync()
    {
        await foreach (var evt in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await _observer.OnEventAsync(evt, CancellationToken.None).ConfigureAwait(false);
                _healthTracker?.RecordSuccess(_observer);
            }
            catch (Exception ex)
            {
                _errorLogger?.LogError(ex,
                    "Observer {ObserverType} failed processing {EventType}",
                    _observer.GetType().Name, evt.GetType().Name);

                _healthTracker?.RecordFailure(_observer, ex);
            }
        }
    }
}

namespace HPD.ML.Core;

using HPD.Events;
using HPD.ML.Abstractions;

/// <summary>
/// Bridges IObservable&lt;ProgressEvent&gt; to HPD-Events.
/// Learner implementations use this to emit progress events that are
/// both observable via Rx and routed through the event coordinator.
/// </summary>
public sealed class ProgressSubject : IObservable<ProgressEvent>, IDisposable
{
    private readonly List<IObserver<ProgressEvent>> _observers = [];
    private readonly IEventCoordinator? _coordinator;
    private readonly object _lock = new();

    public ProgressSubject(IEventCoordinator? coordinator = null)
        => _coordinator = coordinator;

    public IDisposable Subscribe(IObserver<ProgressEvent> observer)
    {
        lock (_lock) _observers.Add(observer);
        return new Unsubscriber(() => { lock (_lock) _observers.Remove(observer); });
    }

    /// <summary>Emit a progress event to all observers and the event coordinator.</summary>
    public void OnNext(ProgressEvent progress)
    {
        _coordinator?.Emit(new TrainingProgressEvent(progress));

        lock (_lock)
        {
            foreach (var observer in _observers)
                observer.OnNext(progress);
        }
    }

    public void OnCompleted()
    {
        lock (_lock)
        {
            foreach (var observer in _observers)
                observer.OnCompleted();
        }
    }

    public void OnError(Exception error)
    {
        lock (_lock)
        {
            foreach (var observer in _observers)
                observer.OnError(error);
        }
    }

    public void Dispose() => OnCompleted();

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}

/// <summary>
/// HPD-Events wrapper for training progress.
/// </summary>
public sealed record TrainingProgressEvent : Event
{
    public ProgressEvent Progress { get; }

    public TrainingProgressEvent(ProgressEvent progress)
    {
        Progress = progress;
        Kind = EventKind.Content;
        Priority = EventPriority.Normal;
    }
}

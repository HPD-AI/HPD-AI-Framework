namespace HPD.Events.Signals;

/// <summary>Local wake primitive for event loops and schedulers.</summary>
public interface IEventSignal
{
    /// <summary>Wait until the signal is raised or cancellation is requested.</summary>
    ValueTask WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>Consume one pending signal without waiting.</summary>
    bool TryConsume();
}

/// <summary>Producer side of a local event-loop signal.</summary>
public interface IEventSignalSource
{
    /// <summary>Raise the signal.</summary>
    void Signal();
}

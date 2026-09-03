namespace HPD.TUI.Core;

/// <summary>Serializes UI mutations through the application's event-loop mailbox.</summary>
public interface ITuiDispatcher
{
    /// <summary>Gets whether the caller is executing on the UI event loop.</summary>
    bool CheckAccess();

    /// <summary>Queues a callback on the UI event loop.</summary>
    void Post(Action callback);

    /// <summary>Queues a callback and completes after it executes on the UI event loop.</summary>
    ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken = default);

    /// <summary>Queues an asynchronous callback and completes after its UI mutation finishes.</summary>
    ValueTask InvokeAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default);
}

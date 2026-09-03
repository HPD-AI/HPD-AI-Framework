using HPD.TUI.Core;
using HPD.Agent;

namespace HPD.Agent.TUI.Markdown;

/// <summary>Serializes agent projection mutations on the application's sole UI mailbox.</summary>
public interface IAgentTuiDispatcher
{
    /// <summary>Gets whether execution is already within the logical UI dispatcher.</summary>
    bool CheckAccess();
    /// <summary>Queues a synchronous UI mutation.</summary>
    void Post(Action callback);
    /// <summary>Queues and awaits a synchronous UI mutation.</summary>
    ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken = default);
    /// <summary>Queues and awaits an asynchronous UI mutation.</summary>
    ValueTask InvokeAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default);
}

/// <summary>Adapts the generic TUI dispatcher without introducing a second queue.</summary>
public sealed class AgentTuiDispatcher(ITuiDispatcher dispatcher) : IAgentTuiDispatcher
{
    private readonly ITuiDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    /// <inheritdoc />
    public bool CheckAccess() => _dispatcher.CheckAccess();
    /// <inheritdoc />
    public void Post(Action callback) => _dispatcher.Post(callback);
    /// <inheritdoc />
    public ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken = default) => _dispatcher.InvokeAsync(callback, cancellationToken);
    /// <inheritdoc />
    public ValueTask InvokeAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default) => _dispatcher.InvokeAsync(callback, cancellationToken);
}

/// <summary>Coordinates newline-gated message sessions and coalesces refresh publication.</summary>
public sealed class MarkdownStreamCoordinator
{
    private readonly IAgentTuiDispatcher _dispatcher;
    private readonly Action<MarkdownStreamUpdate, MarkdownMessageProjection> _publish;
    private readonly Action<string>? _diagnostic;
    private readonly Dictionary<MarkdownStreamIdentity, MarkdownStreamSession> _sessions = [];
    private readonly HashSet<MarkdownStreamIdentity> _refreshQueued = [];
    private readonly HashSet<MarkdownStreamIdentity> _lifecycleOnly = [];
    private readonly Dictionary<MarkdownStreamIdentity, MarkdownMessageState> _terminal = [];

    public MarkdownStreamCoordinator(
        IAgentTuiDispatcher dispatcher,
        Action<MarkdownStreamUpdate, MarkdownMessageProjection> publish,
        Action<string>? diagnostic = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _diagnostic = diagnostic;
    }

    /// <summary>Starts a new lineage for a stream identity.</summary>
    public void Start(MarkdownStreamIdentity identity, MarkdownMessagePresentation? presentation = null)
        => Dispatch(() =>
        {
            if (_sessions.Remove(identity, out var previous))
            {
                var interrupted = previous.Interrupt();
                if (!_lifecycleOnly.Contains(identity) && !string.IsNullOrWhiteSpace(interrupted.Document.GetCanonicalSource()))
                    _publish(interrupted, previous.Projection);
                _diagnostic?.Invoke($"Duplicate Markdown start replaced '{identity.Kind}:{identity.MessageId}'.");
            }
            _refreshQueued.Remove(identity);
            _lifecycleOnly.Remove(identity);
            _terminal.Remove(identity);
            var session = new MarkdownStreamSession(identity, presentation);
            _sessions.Add(identity, session);
            if (presentation?.Visibility == AgentMessageVisibility.Hidden)
                _lifecycleOnly.Add(identity);
        });

    /// <summary>Appends exact source and schedules at most one refresh for the pending batch.</summary>
    public void Append(MarkdownStreamIdentity identity, string delta)
        => Dispatch(() =>
        {
            if (!_sessions.TryGetValue(identity, out var session))
            {
                _diagnostic?.Invoke($"Markdown delta arrived before start for '{identity.Kind}:{identity.MessageId}'.");
                return;
            }
            if (_lifecycleOnly.Contains(identity)) return;
            var change = session.Append(delta);
            if (!change.SourceChanged || !_refreshQueued.Add(identity)) return;
            _dispatcher.Post(() =>
            {
                _refreshQueued.Remove(identity);
                if (!_sessions.TryGetValue(identity, out var current)) return;
                _publish(current.Refresh(), current.Projection);
            });
        });

    /// <summary>Completes an active stream.</summary>
    public void Complete(MarkdownStreamIdentity identity) => Finalize(identity, static session => session.Complete());
    /// <summary>Interrupts an active stream.</summary>
    public void Interrupt(MarkdownStreamIdentity identity) => Finalize(identity, static session => session.Interrupt());
    /// <summary>Cancels an active stream.</summary>
    public void Cancel(MarkdownStreamIdentity identity) => Finalize(identity, static session => session.Cancel());
    /// <summary>Fails an active stream.</summary>
    public void Fail(MarkdownStreamIdentity identity) => Finalize(identity, static session => session.Fail());

    /// <summary>Deterministically closes every active stream during an enclosing lifecycle transition.</summary>
    public void FinalizeAll(MarkdownMessageState state)
        => Dispatch(() =>
        {
            foreach (var pair in _sessions.ToArray())
            {
                var update = state switch
                {
                    MarkdownMessageState.Cancelled => pair.Value.Cancel(),
                    MarkdownMessageState.Failed => pair.Value.Fail(),
                    MarkdownMessageState.Interrupted => pair.Value.Interrupt(),
                    _ => pair.Value.Complete()
                };
                if (!_lifecycleOnly.Contains(pair.Key) && !string.IsNullOrWhiteSpace(update.Document.GetCanonicalSource()))
                    _publish(update, pair.Value.Projection);
                _terminal[pair.Key] = state;
            }
            _refreshQueued.Clear();
            _lifecycleOnly.Clear();
            _sessions.Clear();
        });

    private void Finalize(MarkdownStreamIdentity identity, Func<MarkdownStreamSession, MarkdownStreamUpdate> transition)
        => Dispatch(() =>
        {
            _refreshQueued.Remove(identity);
            if (!_sessions.Remove(identity, out var session))
            {
                if (!_terminal.ContainsKey(identity))
                {
                    _terminal[identity] = MarkdownMessageState.Completed;
                    _diagnostic?.Invoke($"Markdown end arrived before start for '{identity.Kind}:{identity.MessageId}'.");
                }
                return;
            }
            var update = transition(session);
            if (!_lifecycleOnly.Remove(identity) && !string.IsNullOrWhiteSpace(update.Document.GetCanonicalSource()))
                _publish(update, session.Projection);
            _terminal[identity] = update.Document.State;
        });

    /// <summary>Discards all session-owned source after its producer has stopped.</summary>
    public void DiscardAll()
    {
        if (!_dispatcher.CheckAccess())
            throw new InvalidOperationException("Markdown stream state may only be discarded on the TUI dispatcher.");
        _refreshQueued.Clear();
        _lifecycleOnly.Clear();
        _sessions.Clear();
        _terminal.Clear();
    }

    private void Dispatch(Action mutation)
    {
        if (_dispatcher.CheckAccess()) mutation();
        else _dispatcher.Post(mutation);
    }
}

using HPD.Agent;

namespace HPD.Agent.TUI.Markdown;

/// <summary>Owns Markdown stream lineages on the projection worker.</summary>
/// <remarks>This type never dispatches UI work. Its immutable publications are committed by the UI actor.</remarks>
public sealed class MarkdownStreamCoordinator
{
    private readonly Action<MarkdownStreamUpdate, MarkdownMessageProjection> _publish;
    private readonly Action<string>? _diagnostic;
    private readonly Func<MarkdownStreamIdentity, MarkdownMessagePresentation?, IReadOnlyDictionary<string, object?>?, MarkdownStreamSession> _createSession;
    private readonly Dictionary<MarkdownStreamIdentity, MarkdownStreamSession> _sessions = [];
    private readonly HashSet<MarkdownStreamIdentity> _dirty = [];
    private readonly HashSet<MarkdownStreamIdentity> _lifecycleOnly = [];
    private readonly Dictionary<MarkdownStreamIdentity, MarkdownMessageState> _terminal = [];

    /// <summary>Creates a worker-owned stream coordinator.</summary>
    public MarkdownStreamCoordinator(Action<MarkdownStreamUpdate, MarkdownMessageProjection> publish,
        Action<string>? diagnostic = null)
        : this(publish, static (identity, presentation, properties) =>
            new MarkdownStreamSession(identity, presentation, additionalProperties: properties), diagnostic)
    {
    }

    internal MarkdownStreamCoordinator(
        Action<MarkdownStreamUpdate, MarkdownMessageProjection> publish,
        Func<MarkdownStreamIdentity, MarkdownMessagePresentation?, IReadOnlyDictionary<string, object?>?, MarkdownStreamSession> createSession,
        Action<string>? diagnostic = null)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _createSession = createSession ?? throw new ArgumentNullException(nameof(createSession));
        _diagnostic = diagnostic;
    }

    /// <summary>Starts a lineage, interrupting a matching active lineage first.</summary>
    public void Start(MarkdownStreamIdentity identity, MarkdownMessagePresentation? presentation = null,
        IReadOnlyDictionary<string, object?>? additionalProperties = null)
    {
        if (_sessions.Remove(identity, out var previous))
        {
            var interrupted = previous.Interrupt();
            if (!string.IsNullOrWhiteSpace(interrupted.Document.GetCanonicalSource())) _publish(interrupted, previous.Projection);
            _diagnostic?.Invoke($"Duplicate Markdown start replaced '{identity.Kind}:{identity.MessageId}'.");
        }
        _dirty.Remove(identity);
        _lifecycleOnly.Remove(identity);
        _terminal.Remove(identity);
        if (presentation?.Visibility == AgentMessageVisibility.Hidden) { _lifecycleOnly.Add(identity); return; }
        _sessions.Add(identity, _createSession(identity, presentation, additionalProperties));
    }

    /// <summary>Appends exact source without parsing or publishing it.</summary>
    public void Append(MarkdownStreamIdentity identity, string delta)
    {
        if (_lifecycleOnly.Contains(identity)) return;
        if (!_sessions.TryGetValue(identity, out var session))
        {
            _diagnostic?.Invoke($"Markdown delta arrived before start for '{identity.Kind}:{identity.MessageId}'.");
            return;
        }
        if (session.Append(delta).SourceChanged) _dirty.Add(identity);
    }

    /// <summary>Publishes the newest revision of every dirty active lineage.</summary>
    public void RefreshPending()
    {
        foreach (var identity in _dirty.ToArray())
        {
            _dirty.Remove(identity);
            if (_sessions.TryGetValue(identity, out var session)) _publish(session.Refresh(), session.Projection);
        }
    }

    /// <summary>Completes an active stream.</summary>
    public void Complete(MarkdownStreamIdentity identity) => Finalize(identity, MarkdownMessageState.Completed, static s => s.Complete());
    /// <summary>Interrupts an active stream.</summary>
    public void Interrupt(MarkdownStreamIdentity identity) => Finalize(identity, MarkdownMessageState.Interrupted, static s => s.Interrupt());
    /// <summary>Cancels an active stream.</summary>
    public void Cancel(MarkdownStreamIdentity identity) => Finalize(identity, MarkdownMessageState.Cancelled, static s => s.Cancel());
    /// <summary>Fails an active stream.</summary>
    public void Fail(MarkdownStreamIdentity identity) => Finalize(identity, MarkdownMessageState.Failed, static s => s.Fail());

    /// <summary>Closes every active stream during an enclosing lifecycle transition.</summary>
    public void FinalizeAll(MarkdownMessageState state)
    {
        foreach (var pair in _sessions.ToArray())
        {
            var update = state switch
            {
                MarkdownMessageState.Cancelled => pair.Value.Cancel(), MarkdownMessageState.Failed => pair.Value.Fail(),
                MarkdownMessageState.Interrupted => pair.Value.Interrupt(), _ => pair.Value.Complete()
            };
            if (!string.IsNullOrWhiteSpace(update.Document.GetCanonicalSource())) _publish(update, pair.Value.Projection);
            _terminal[pair.Key] = state;
        }
        foreach (var identity in _lifecycleOnly) _terminal[identity] = state;
        _dirty.Clear(); _lifecycleOnly.Clear(); _sessions.Clear();
    }

    internal bool TryGetTerminalState(MarkdownStreamIdentity identity, out MarkdownMessageState state)
        => _terminal.TryGetValue(identity, out state);

    /// <summary>Discards all worker-owned state after its producer has stopped.</summary>
    internal void DiscardAllAfterProducerStopped()
    { _dirty.Clear(); _lifecycleOnly.Clear(); _sessions.Clear(); _terminal.Clear(); }

    private void Finalize(MarkdownStreamIdentity identity, MarkdownMessageState terminalState,
        Func<MarkdownStreamSession, MarkdownStreamUpdate> transition)
    {
        _dirty.Remove(identity);
        if (_lifecycleOnly.Remove(identity)) { _terminal[identity] = terminalState; return; }
        if (!_sessions.Remove(identity, out var session))
        {
            if (!_terminal.ContainsKey(identity))
            {
                _terminal[identity] = terminalState;
                _diagnostic?.Invoke($"Markdown end arrived before start for '{identity.Kind}:{identity.MessageId}'.");
            }
            return;
        }
        var update = transition(session);
        if (!string.IsNullOrWhiteSpace(update.Document.GetCanonicalSource())) _publish(update, session.Projection);
        _terminal[identity] = update.Document.State;
    }
}

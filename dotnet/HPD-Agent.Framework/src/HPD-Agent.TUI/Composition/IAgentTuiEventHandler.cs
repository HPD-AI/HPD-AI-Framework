using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Composition;

/// <summary>Staged shell output for one projection contribution.</summary>
public sealed class AgentTuiProjectionShell
{
    private readonly ChatShellModel _source;

    internal AgentTuiProjectionShell(ChatShellModel source)
    {
        _source = source;
        Transcript = source.Transcript.CreateProjectionCopy();
        PromptStatusText = source.PromptStatusText;
        Activities = new HPD.TUI.Models.ActivityGroupModel();
        Activities.RestoreCheckpoint(source.Activities.CaptureCheckpoint());
    }

    public TranscriptModel Transcript { get; }

    public string PromptStatusText { get; set; }

    public HPD.TUI.Models.ActivityGroupModel Activities { get; }

    public IHpdAgentTuiRuntime? Runtime => _source.Runtime;

    public AgentTuiExecutionTarget Target => _source.Target;
}

/// <summary>Inputs and isolated staging outputs for one TUI projection contribution.</summary>
public sealed class AgentTuiProjectionContext
{
    private readonly ChatShellModel _shell;
    private readonly AgentTuiProjectionStateStore _state;
    private readonly Func<string, bool> _isContributionActive;
    private readonly Action<string, Exception> _contributionFailed;
    private readonly Action<Func<AgentTuiProjectionContext, ValueTask>> _enqueueProjection;
    private readonly Action _requestRenderAfterCommit;
    private bool _renderRequested;
    private bool _committed;

    public AgentTuiProjectionContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        HpdAgentTuiRegistry registry,
        AgentTuiStateBag state,
        AgentTuiEventDeliveryMode deliveryMode = AgentTuiEventDeliveryMode.Live,
        Action? requestRender = null,
        ToolCallArguments? toolArguments = null,
        Func<string, bool>? isContributionActive = null,
        Action<string, Exception>? contributionFailed = null,
        Action<Func<AgentTuiProjectionContext, ValueTask>>? enqueueProjection = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _state = state?.BeginProjection() ?? throw new ArgumentNullException(nameof(state));
        SharedServices = new AgentTuiSharedServices(state);
        DeliveryMode = deliveryMode;
        _requestRenderAfterCommit = requestRender ?? (static () => { });
        ToolArguments = toolArguments;
        _isContributionActive = isContributionActive ?? (static _ => true);
        _contributionFailed = contributionFailed ?? (static (_, _) => { });
        _enqueueProjection = enqueueProjection ?? (static _ => throw new InvalidOperationException(
            "This projection context has no host scheduler for deferred projections."));
        Shell = new AgentTuiProjectionShell(shell);
    }

    public AgentTuiRuntimeScope Scope { get; }

    public AgentTuiProjectionShell Shell { get; }

    public HpdAgentTuiRegistry Registry { get; }

    public IAgentTuiStateStore State => _state;

    public AgentTuiSharedServices SharedServices { get; }

    public AgentTuiEventDeliveryMode DeliveryMode { get; }

    public ToolCallArguments? ToolArguments { get; }

    /// <summary>Requests a render after the contribution commits successfully.</summary>
    public Action RequestRender => RequestRenderDeferred;

    /// <summary>
    /// Enqueues a later projection against the latest committed shell and state.
    /// The host runs it after the current contribution transaction has committed.
    /// </summary>
    public void EnqueueProjection(
        Func<AgentTuiProjectionContext, ValueTask> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _enqueueProjection(projection);
    }

    private void RequestRenderDeferred()
    {
        if (_committed)
            _requestRenderAfterCommit();
        else
            _renderRequested = true;
    }

    public bool IsContributionActive(string contributionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contributionKey);
        return _isContributionActive(contributionKey);
    }

    public bool HasActiveToolCallHandler(
        AgentEvent evt,
        string? toolHarnessName,
        string toolName,
        ToolCallType? callType)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        foreach (var candidate in Registry.EventHandlers)
        {
            if (!candidate.Scope.Includes(evt, Scope) ||
                !IsContributionActive(candidate.Key) ||
                candidate.Value is not IAgentTuiToolCallHandler handler)
            {
                continue;
            }

            try
            {
                if (handler.CanHandleToolCall(toolHarnessName, toolName, callType))
                    return true;
            }
            catch (Exception ex)
            {
                _contributionFailed(candidate.Key, ex);
            }
        }

        return false;
    }

    internal void Commit()
    {
        if (_committed)
            throw new InvalidOperationException("A projection contribution can only be committed once.");

        _shell.Transcript.CommitProjectionCopy(Shell.Transcript);
        _shell.PromptStatusText = Shell.PromptStatusText;
        _shell.Activities.RestoreCheckpoint(Shell.Activities.CaptureCheckpoint());
        _state.Commit();
        _committed = true;
    }

    internal void FlushDeferredRenderRequest()
    {
        if (!_renderRequested)
            return;

        _renderRequested = false;
        _requestRenderAfterCommit();
    }
}

public interface IAgentTuiEventHandler
{
    bool CanHandle(AgentEvent evt);

    ValueTask ProjectAsync(
        AgentEvent evt,
        AgentTuiProjectionContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Declares that an event handler provides the specialized transcript projection for selected tool calls.
/// Generic renderers can use this ownership signal to avoid rendering the same call twice.
/// </summary>
public interface IAgentTuiToolCallHandler
{
    bool CanHandleToolCall(
        string? toolHarnessName,
        string toolName,
        ToolCallType? callType);
}

public abstract class AgentTuiEventHandler<TEvent> : IAgentTuiEventHandler
    where TEvent : AgentEvent
{
    public bool CanHandle(AgentEvent evt) => evt is TEvent;

    public abstract ValueTask ProjectAsync(
        TEvent evt,
        AgentTuiProjectionContext context,
        CancellationToken cancellationToken);

    ValueTask IAgentTuiEventHandler.ProjectAsync(
        AgentEvent evt,
        AgentTuiProjectionContext context,
        CancellationToken cancellationToken)
        => evt is TEvent typed
            ? ProjectAsync(typed, context, cancellationToken)
            : ValueTask.CompletedTask;
}

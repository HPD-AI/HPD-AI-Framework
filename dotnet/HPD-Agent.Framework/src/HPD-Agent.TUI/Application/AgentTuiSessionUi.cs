using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.Agent;

namespace HPD.Agent.TUI.Application;

internal sealed class AgentTuiSessionUi : IAgentTuiSessionUi
{
    private readonly AgentTuiSessionUiController _controller;
    private readonly HpdContributionOwner _owner;
    private readonly AgentTuiSessionUiGeneration _generation;
    private readonly AgentTuiRuntimeScope _scope;
    private readonly ChatShellModel _shell;
    private readonly AgentTuiStateBag _state;
    private readonly HpdAgentTuiRegistry _registry;
    private readonly IAgentTuiDialogService _dialogs;
    private readonly Func<string, CancellationToken, ValueTask> _setPromptDraftAsync;
    private readonly Action _requestRender;

    public AgentTuiSessionUi(
        AgentTuiSessionUiController controller,
        HpdContributionOwner owner,
        AgentTuiSessionUiGeneration generation,
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiStateBag state,
        HpdAgentTuiRegistry registry,
        IAgentTuiDialogService dialogs,
        Func<string, CancellationToken, ValueTask> setPromptDraftAsync,
        Action requestRender)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _generation = generation;
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _setPromptDraftAsync = setPromptDraftAsync ?? throw new ArgumentNullException(nameof(setPromptDraftAsync));
        _requestRender = requestRender ?? throw new ArgumentNullException(nameof(requestRender));
    }

    public AgentTuiSessionUiContext Context
    {
        get
        {
            EnsureCurrent();
            return new AgentTuiSessionUiContext
            {
                Owner = _owner,
                Scope = _scope,
                Shell = _shell,
                State = _state,
                Registry = _registry
            };
        }
    }

    public void SetStatus(string key, string? text)
    {
        EnsureCurrent();
        _shell.Status.Set(key, text, _owner);
        _requestRender();
    }

    public void SetTemporaryWidget(
        string key,
        IAgentTuiWidget? widget,
        TuiSlot slot = TuiSlot.AboveEditor)
    {
        EnsureCurrent();
        var model = GetWidgetSlot(slot);
        if (widget is null)
        {
            model.Set(key, null, _owner);
            _requestRender();
            return;
        }

        var component = widget.Create(new AgentTuiWidgetContext(slot, _scope, _shell, _state));
        model.Set(key, component, _owner);
        _requestRender();
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        EnsureCurrent();
        var label = string.IsNullOrWhiteSpace(message)
            ? title
            : $"{title}\n{message}";
        return await _dialogs.ConfirmAsync(label, cancellationToken: cancellationToken)
            .ConfigureAwait(false) == true;
    }

    public async Task<string?> PromptAsync(
        string title,
        string? placeholder = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCurrent();
        return await _dialogs.InputAsync(title, placeholder, allowEmpty: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Notify(
        string message,
        TranscriptSeverity severity = TranscriptSeverity.Info)
    {
        EnsureCurrent();
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _shell.Transcript.AddFinal(new TranscriptEntry(
            Id: $"session-ui-notice-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: new NoticeCell(message, Severity: severity),
            Metadata: new TranscriptEntryMetadata(
                AgentId: _scope.AgentId,
                AgentName: _owner.DisplayName ?? _owner.Id,
                AgentChain: [_owner.Id])));
        _requestRender();
    }

    public void SetWorkingMessage(string? message)
    {
        EnsureCurrent();
        _shell.FooterText = string.IsNullOrWhiteSpace(message) ? "" : message;
        _requestRender();
    }

    public ValueTask SetPromptDraftAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        EnsureCurrent();
        return _setPromptDraftAsync(text, cancellationToken);
    }

    public void RequestRender()
    {
        EnsureCurrent();
        _requestRender();
    }

    private WidgetSlotModel GetWidgetSlot(TuiSlot slot)
        => slot switch
        {
            TuiSlot.AboveEditor => _shell.AboveEditor,
            TuiSlot.BelowEditor => _shell.BelowEditor,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
        };

    private void EnsureCurrent()
        => _controller.ThrowIfStale(_owner, _generation);
}

internal sealed class AgentTuiSessionUiController
{
    private long _globalGeneration;
    private readonly Dictionary<HpdContributionOwner, long> _ownerGenerations = [];

    public AgentTuiSessionUiGeneration GetGeneration(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new AgentTuiSessionUiGeneration(
            Interlocked.Read(ref _globalGeneration),
            _ownerGenerations.TryGetValue(owner, out var ownerGeneration)
                ? ownerGeneration
                : 0);
    }

    public void ThrowIfStale(HpdContributionOwner owner, AgentTuiSessionUiGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (generation != GetGeneration(owner))
        {
            throw new InvalidOperationException(
                $"Session UI handle for '{owner.Id}' is stale after TUI session UI generation {GetGeneration(owner)}.");
        }
    }

    public void InvalidateAll(ChatShellModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        Interlocked.Increment(ref _globalGeneration);
        _ownerGenerations.Clear();
        shell.Status.ClearOwned();
        shell.AboveEditor.ClearOwned();
        shell.BelowEditor.ClearOwned();
    }

    public void InvalidateOwner(ChatShellModel shell, HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(owner);
        _ownerGenerations[owner] = _ownerGenerations.TryGetValue(owner, out var ownerGeneration)
            ? ownerGeneration + 1
            : 1;
        ClearOwner(shell, owner);
    }

    public void ClearOwner(ChatShellModel shell, HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(owner);
        shell.Status.ClearOwner(owner);
        shell.AboveEditor.ClearOwner(owner);
        shell.BelowEditor.ClearOwner(owner);
    }
}

internal readonly record struct AgentTuiSessionUiGeneration(
    long Global,
    long Owner);

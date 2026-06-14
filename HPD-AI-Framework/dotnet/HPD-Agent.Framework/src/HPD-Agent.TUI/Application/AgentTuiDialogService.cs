using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Flows;
using HPD.TUI.Layout;

namespace HPD.Agent.TUI.Application;

internal sealed class AgentTuiDialogService : IAgentTuiDialogService
{
    private readonly DialogHost _host;
    private readonly AgentTuiDialogChrome _chrome;
    private readonly WidgetSlotModel _inlineSlot;
    private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);

    public AgentTuiDialogService(
        DialogHost host,
        AgentTuiDialogChrome chrome,
        WidgetSlotModel inlineSlot)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _inlineSlot = inlineSlot ?? throw new ArgumentNullException(nameof(inlineSlot));
    }

    public bool HasOpenDialog => _host.HasOpenDialog;

    public void Show(string key, IComponent component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(component);
        if (_keys.ContainsKey(key))
        {
            throw new InvalidOperationException($"A dialog is already open for '{key}'.");
        }

        var card = CreateDialogCard(component);
        _keys[key] = _host.Count;
        _inlineSlot.Add(card);
        _host.PushInline(card, component, () =>
        {
            _inlineSlot.Remove(card);
            _keys.Remove(key);
        });
    }

    public bool Close(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_keys.TryGetValue(key, out var layerIndex) || layerIndex != _host.Count - 1)
        {
            return false;
        }

        return _host.Pop();
    }

    public bool CloseTop()
        => _host.Pop();

    public async Task<bool?> ConfirmAsync(
        string title,
        bool? defaultValue = null,
        CancellationToken cancellationToken = default)
    {
        var flow = PromptFlow.Confirm(title);
        if (defaultValue is { } value)
        {
            flow.Default(value);
        }

        return await RunPromptAsync(flow, cancellationToken).ConfigureAwait(false);
    }

    public Task<T?> SelectAsync<T>(
        string title,
        IReadOnlyList<T> options,
        Func<T, string> titleSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(titleSelector);
        if (options.Count == 0)
        {
            return Task.FromResult<T?>(default);
        }

        return RunPromptAsync(PromptFlow.Select(title, options, titleSelector), cancellationToken);
    }

    public Task<string?> InputAsync(
        string title,
        string? defaultValue = null,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default)
    {
        var flow = PromptFlow.Text(title).AllowEmpty(allowEmpty);
        if (!string.IsNullOrEmpty(defaultValue))
        {
            flow.Default(defaultValue);
        }

        return RunPromptAsync(flow, cancellationToken);
    }

    private Task<T?> RunPromptAsync<T>(
        PromptFlow<T> flow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var layerIndex = _host.Count;
        var component = flow.CreateComponentForTesting(result =>
        {
            if (result.IsSubmitted)
            {
                completion.TrySetResult(result.Value);
                PopTo(layerIndex);
                return;
            }

            completion.TrySetResult(default);
            PopTo(layerIndex);
        });

        var registration = cancellationToken.Register(() =>
        {
            completion.TrySetCanceled(cancellationToken);
            PopTo(layerIndex);
        });
        completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var card = CreateDialogCard(component);
        _inlineSlot.Add(card);
        _host.PushInline(card, component, () =>
        {
            _inlineSlot.Remove(card);
            completion.TrySetResult(default);
        });

        return completion.Task;
    }

    private IComponent CreateDialogCard(IComponent component)
        => Frame.Create(component)
            .WithBorder(BorderSpec.Rounded)
            .WithPadding(new Thickness(0, 1))
            .WithSize(_chrome.Width, _chrome.Height);

    private void PopTo(int initialCount)
    {
        while (_host.Count > initialCount)
        {
            _host.Pop();
        }
    }
}

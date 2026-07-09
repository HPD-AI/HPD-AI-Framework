using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Flows;
using HPD.TUI.Layout;
using HPD.TUI.Models;

namespace HPD.Agent.TUI.Application;

internal sealed class AgentTuiDialogService : IAgentTuiDialogService
{
    private readonly DialogHost _host;
    private readonly AgentTuiDialogChrome _chrome;
    private readonly WidgetSlotModel _inlineSlot;
    private readonly AgentTuiNavigationModel _navigation;
    private readonly Action _requestRender;
    private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);

    public AgentTuiDialogService(
        DialogHost host,
        AgentTuiDialogChrome chrome,
        WidgetSlotModel inlineSlot,
        AgentTuiNavigationModel navigation,
        Action? requestRender = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _inlineSlot = inlineSlot ?? throw new ArgumentNullException(nameof(inlineSlot));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _requestRender = requestRender ?? (() => { });
    }

    public bool HasOpenDialog => _host.HasOpenDialog;

    public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
        string key,
        Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(componentFactory);
        if (_keys.ContainsKey(key))
        {
            throw new InvalidOperationException($"A dialog is already open for '{key}'.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AgentTuiDialogResult<TResult>>(cancellationToken);
        }

        var completion = new TaskCompletionSource<AgentTuiDialogResult<TResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var layerIndex = _host.Count;
        var completed = 0;

        void Complete(AgentTuiDialogResult<TResult> result)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
            {
                return;
            }

            completion.TrySetResult(result);
            PopTo(layerIndex);
        }

        var dialogContext = new AgentTuiDialogContext<TResult>(key, _navigation, Complete);
        var component = componentFactory(dialogContext);
        var card = CreateDialogCard(component);
        var registration = cancellationToken.Register(() =>
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
            {
                return;
            }

            completion.TrySetCanceled(cancellationToken);
            PopTo(layerIndex);
        });
        completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        _keys[key] = layerIndex;
        _inlineSlot.Add(card);
        var frameId = _navigation.PushDialog(key, () => PopTo(layerIndex));
        _host.PushInline(card, component, () =>
        {
            _navigation.RemoveDialog(frameId);
            _inlineSlot.Remove(card);
            _keys.Remove(key);
            _requestRender();
            if (Interlocked.Exchange(ref completed, 1) == 0)
            {
                completion.TrySetResult(AgentTuiDialogResult<TResult>.Dismissed());
            }
        });
        _requestRender();

        return completion.Task;
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

    public async Task<AgentTuiDialogResult<bool>> ConfirmAsync(
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

    public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
        string title,
        IReadOnlyList<T> options,
        Func<T, string> titleSelector,
        CancellationToken cancellationToken = default)
        => SelectAsync(title, options, titleSelector, AgentTuiSelectOptions.Default, cancellationToken);

    public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
        string title,
        IReadOnlyList<T> options,
        Func<T, string> titleSelector,
        AgentTuiSelectOptions selectOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(titleSelector);
        ArgumentNullException.ThrowIfNull(selectOptions);
        if (options.Count == 0)
        {
            return Task.FromResult(AgentTuiDialogResult<T>.Dismissed());
        }

        if (!selectOptions.AllowFilter)
        {
            return RunPromptAsync(PromptFlow.Select(title, options, titleSelector), cancellationToken);
        }

        var model = new SelectionModel<T> { AllowFilter = true };
        foreach (var option in options)
        {
            model.Add(option, titleSelector(option));
        }

        return RunPromptAsync(PromptFlow.Select(title, model), cancellationToken);
    }

    public Task<AgentTuiDialogResult<string>> InputAsync(
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

    public Task<AgentTuiDialogResult<string>> SecretInputAsync(
        string title,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default)
    {
        var flow = PromptFlow.Secret(title).AllowEmpty(allowEmpty);
        return RunPromptAsync(flow, cancellationToken);
    }

    private Task<AgentTuiDialogResult<T>> RunPromptAsync<T>(
        PromptFlow<T> flow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var completion = new TaskCompletionSource<AgentTuiDialogResult<T>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var layerIndex = _host.Count;
        var completed = 0;
        IComponent? card = null;
        string? frameId = null;

        void Cleanup()
        {
            if (frameId is not null)
            {
                _navigation.RemoveDialog(frameId);
            }

            if (card is not null)
            {
                _inlineSlot.Remove(card);
            }

            _requestRender();
        }

        void CloseLayer()
        {
            if (_host.Count > layerIndex)
            {
                PopTo(layerIndex);
                return;
            }

            Cleanup();
        }

        void Complete(AgentTuiDialogResult<T> result)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
            {
                return;
            }

            completion.TrySetResult(result);
            CloseLayer();
        }

        var component = flow.CreateComponentForTesting(result =>
        {
            if (result.IsSubmitted)
            {
                Complete(AgentTuiDialogResult<T>.Submitted(result.Value!));
                return;
            }

            Complete(AgentTuiDialogResult<T>.Back());
        });

        var registration = cancellationToken.Register(() =>
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
            {
                return;
            }

            completion.TrySetCanceled(cancellationToken);
            CloseLayer();
        });
        completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        card = CreateDialogCard(component);
        _inlineSlot.Add(card);
        frameId = _navigation.PushDialog("Prompt", CloseLayer);
        _host.PushInline(card, component, () =>
        {
            Cleanup();
            if (Interlocked.Exchange(ref completed, 1) == 0)
            {
                completion.TrySetResult(AgentTuiDialogResult<T>.Dismissed());
            }
        });
        _requestRender();

        return completion.Task;
    }

    private IComponent CreateDialogCard(IComponent component)
        => Frame.Create(component)
            .WithBorder(BorderSpec.Rounded)
            .WithPadding(new Thickness(0, 1))
            .WithSize(_chrome.Width > 0 ? _chrome.Width : int.MaxValue);

    private void PopTo(int initialCount)
    {
        while (_host.Count > initialCount)
        {
            _host.Pop();
        }
        _requestRender();
    }
}

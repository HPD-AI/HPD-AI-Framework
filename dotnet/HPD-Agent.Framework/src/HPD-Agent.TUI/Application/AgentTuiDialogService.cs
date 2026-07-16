using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Flows;
using HPD.TUI.Forms;
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
        return RunDialogAsync<TResult>(
            key,
            key,
            trackKey: true,
            complete =>
            {
                var context = new AgentTuiDialogContext<TResult>(key, _navigation, complete);
                var component = componentFactory(context);
                return new DialogContent(component, component, FocusHandlesEscape: false);
            },
            cancellationToken);
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

    public bool CloseTop() => _host.Pop();

    public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
        string title,
        bool? defaultValue = null,
        CancellationToken cancellationToken = default)
    {
        var flow = PromptFlow.Confirm(title);
        if (defaultValue is { } value)
        {
            flow.Default(value);
        }

        return ShowPromptAsync(title, flow, cancellationToken);
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
            return ShowPromptAsync(title, PromptFlow.Select(title, options, titleSelector), cancellationToken);
        }

        var model = new SelectionModel<T> { AllowFilter = true };
        foreach (var option in options)
        {
            model.Add(option, titleSelector(option));
        }

        return ShowPromptAsync(title, PromptFlow.Select(title, model), cancellationToken);
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

        return ShowPromptAsync(title, flow, cancellationToken);
    }

    public Task<AgentTuiDialogResult<string>> SecretInputAsync(
        string title,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default)
        => ShowPromptAsync(title, PromptFlow.Secret(title).AllowEmpty(allowEmpty), cancellationToken);

    public Task<AgentTuiDialogResult<TResult>> FormAsync<TResult>(
        string title,
        FormDefinition<TResult> form,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(form);
        return RunDialogAsync<TResult>(
            $"form:{Guid.NewGuid():N}",
            title,
            trackKey: false,
            complete =>
            {
                var controller = new FormController(form.Model)
                { };
                var updates = new FormUpdateSession<TResult>(
                    form,
                    controller,
                    _requestRender,
                    cancellationToken);
                var finishing = 0;

                void Finish(AgentTuiDialogResult<TResult> result)
                {
                    if (Interlocked.Exchange(ref finishing, 1) != 0)
                    {
                        return;
                    }

                    _ = FinishAsync(result);
                }

                async Task FinishAsync(AgentTuiDialogResult<TResult> result)
                {
                    if (!await updates.FlushAsync().ConfigureAwait(false))
                    {
                        Interlocked.Exchange(ref finishing, 0);
                        _requestRender();
                        return;
                    }

                    complete(result);
                }

                controller.Submitted = _ => Finish(AgentTuiDialogResult<TResult>.Submitted(form.BuildResult()));
                controller.Canceled = () => Finish(AgentTuiDialogResult<TResult>.Canceled());
                var view = new FormView(form.Model, controller, updateMode: form.UpdateMode);
                return new DialogContent(
                    view,
                    view,
                    FocusHandlesEscape: true,
                    Closed: updates.Dispose);
            },
            cancellationToken);
    }

    private Task<AgentTuiDialogResult<T>> ShowPromptAsync<T>(
        string title,
        PromptFlow<T> flow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return RunDialogAsync<T>(
            $"prompt:{Guid.NewGuid():N}",
            title,
            trackKey: false,
            complete =>
            {
                var component = flow.CreateComponentForTesting(result =>
                {
                    complete(result.IsSubmitted
                        ? AgentTuiDialogResult<T>.Submitted(result.Value!)
                        : AgentTuiDialogResult<T>.Back());
                });
                return new DialogContent(component, component, FocusHandlesEscape: false);
            },
            cancellationToken);
    }

    private Task<AgentTuiDialogResult<TResult>> RunDialogAsync<TResult>(
        string key,
        string navigationTitle,
        bool trackKey,
        Func<Action<AgentTuiDialogResult<TResult>>, DialogContent> contentFactory,
        CancellationToken cancellationToken)
    {
        if (trackKey && _keys.ContainsKey(key))
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

        var content = contentFactory(Complete);
        var card = CreateDialogCard(content.Component);
        var registration = cancellationToken.Register(() =>
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
            {
                return;
            }

            completion.TrySetCanceled(cancellationToken);
            PopTo(layerIndex);
        });
        _ = completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        if (trackKey)
        {
            _keys[key] = layerIndex;
        }

        _inlineSlot.Add(card);
        var frameId = _navigation.PushDialog(navigationTitle, () => PopTo(layerIndex));
        _host.PushInline(
            card,
            content.Focus ?? content.Component,
            () =>
            {
                _navigation.RemoveDialog(frameId);
                _inlineSlot.Remove(card);
                content.Closed?.Invoke();
                if (trackKey)
                {
                    _keys.Remove(key);
                }

                _requestRender();
                if (Interlocked.Exchange(ref completed, 1) == 0)
                {
                    completion.TrySetResult(AgentTuiDialogResult<TResult>.Dismissed());
                }
            },
            content.FocusHandlesEscape);
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

    private sealed record DialogContent(
        IComponent Component,
        IComponent? Focus,
        bool FocusHandlesEscape,
        Action? Closed = null);
}

using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Rendering;

namespace HPD.TUI.Flows;

public sealed class ModalSession<T>
{
    private readonly TaskCompletionSource<PromptResult<T>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;

    public bool IsCompleted => _completed;

    public void Complete(PromptResult<T> result)
    {
        _completed = true;
        _completion.TrySetResult(result);
    }

    public void Submit(T value) => Complete(PromptResult<T>.Submitted(value));

    public void Cancel() => Complete(PromptResult<T>.Canceled());

    public async Task<PromptResult<T>> RunAsync(
        TuiApplication app,
        IComponent component,
        IComponent? initialFocus = null,
        TuiRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(component);

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previousRoot = app.Root;
        var previousFocus = app.Focused;
        var host = previousRoot as DialogHost;
        var initialDialogCount = host?.Count ?? 0;

        using var registration = cancellationToken.Register(() =>
        {
            Complete(PromptResult<T>.Canceled());
            sessionCts.Cancel();
        });

        if (host is not null)
        {
            host.Push(new Overlay(component, 0, 0, 80), initialFocus, () =>
            {
                if (!_completed)
                {
                    Cancel();
                    sessionCts.Cancel();
                }
            });
        }
        else
        {
            app.SetRoot(component);
            app.SetFocus(initialFocus);
        }

        try
        {
            var runTask = app.RunAsync(options, sessionCts.Token);
            await Task.WhenAny(_completion.Task, runTask).ConfigureAwait(false);
            if (!_completion.Task.IsCompleted)
            {
                Cancel();
            }

            sessionCts.Cancel();
            await runTask.ConfigureAwait(false);
            return await _completion.Task.ConfigureAwait(false);
        }
        finally
        {
            if (host is not null)
            {
                while (host.Count > initialDialogCount)
                {
                    host.Pop();
                }
            }
            else if (previousRoot is null)
            {
                app.ClearRoot();
            }
            else
            {
                app.SetRoot(previousRoot);
                app.SetFocus(previousFocus);
            }
        }
    }
}

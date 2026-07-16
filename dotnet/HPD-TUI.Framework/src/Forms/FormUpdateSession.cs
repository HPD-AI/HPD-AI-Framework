namespace HPD.TUI.Forms;

public sealed class FormUpdateSession<TResult> : IDisposable
{
    private readonly object _gate = new();
    private readonly FormDefinition<TResult> _form;
    private readonly FormController _controller;
    private readonly Action _requestRender;
    private readonly CancellationToken _cancellationToken;
    private TaskCompletionSource<bool> _idle = CompletedIdle();
    private TResult? _latest;
    private long _latestVersion;
    private bool _hasLatest;
    private bool _running;
    private string? _lastError;
    private bool _disposed;

    public FormUpdateSession(
        FormDefinition<TResult> form,
        FormController controller,
        Action? requestRender = null,
        CancellationToken cancellationToken = default)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _requestRender = requestRender ?? (() => { });
        _cancellationToken = cancellationToken;

        if (_form.UpdateMode == FormUpdateMode.Live)
        {
            _form.Model.Changed += OnFormChanged;
        }
    }

    public async ValueTask<bool> FlushAsync()
    {
        foreach (var field in _form.Model.Fields)
        {
            await field.WaitForPendingChangeAsync().ConfigureAwait(false);
        }

        Task<bool> idle;
        lock (_gate)
        {
            idle = _idle.Task;
        }

        return await idle.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _form.Model.Changed -= OnFormChanged;
    }

    private void OnFormChanged(FormModel model, IFormField field)
    {
        if (_disposed || _form.UpdateMode != FormUpdateMode.Live)
        {
            return;
        }

        if (!_controller.Validate().IsValid)
        {
            _requestRender();
            return;
        }

        var result = _form.BuildResult();
        var startWorker = false;
        lock (_gate)
        {
            _latest = result;
            _hasLatest = true;
            _latestVersion++;
            _lastError = null;
            if (!_running)
            {
                _running = true;
                _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                startWorker = true;
            }
        }

        if (startWorker)
        {
            _ = ProcessUpdatesAsync();
        }
    }

    private async Task ProcessUpdatesAsync()
    {
        while (true)
        {
            TResult update;
            long version;
            lock (_gate)
            {
                update = _latest!;
                version = _latestVersion;
                _hasLatest = false;
            }

            try
            {
                await _form.UpdateAsync!(update, _cancellationToken).ConfigureAwait(false);
                _form.Model.SetUpdateError(null);
                lock (_gate)
                {
                    _lastError = null;
                }
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                Complete(success: false);
                return;
            }
            catch (Exception exception)
            {
                _form.Model.SetUpdateError($"Could not save changes: {exception.Message}");
                lock (_gate)
                {
                    _lastError = exception.Message;
                }
            }

            _requestRender();

            TaskCompletionSource<bool>? completedIdle = null;
            bool success;
            lock (_gate)
            {
                if (_hasLatest && _latestVersion != version)
                {
                    continue;
                }

                _running = false;
                success = _lastError is null;
                completedIdle = _idle;
            }

            completedIdle.TrySetResult(success);
            return;
        }
    }

    private void Complete(bool success)
    {
        TaskCompletionSource<bool> idle;
        lock (_gate)
        {
            _running = false;
            idle = _idle;
        }

        idle.TrySetResult(success);
    }

    private static TaskCompletionSource<bool> CompletedIdle()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult(true);
        return completion;
    }
}

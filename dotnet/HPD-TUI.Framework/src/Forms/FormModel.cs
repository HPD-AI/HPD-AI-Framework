namespace HPD.TUI.Forms;

public sealed class FormModel
{
    private readonly List<IFormField> _fields = [];
    private int _activeFieldIndex;

    public event Action<FormModel, IFormField>? Changed;

    public IReadOnlyList<IFormField> Fields => _fields;

    public int ActiveFieldIndex
    {
        get
        {
            ReconcileActiveField();
            return _activeFieldIndex;
        }
        set
        {
            _activeFieldIndex = _fields.Count == 0
                ? 0
                : Math.Clamp(value, 0, _fields.Count - 1);
            ReconcileActiveField();
        }
    }

    public bool IsDirty => _fields.Any(static item => item.IsDirty);

    public string? UpdateError { get; private set; }

    public int VisibleFieldCount
    {
        get
        {
            var count = 0;
            foreach (var item in _fields)
            {
                if (item.IsVisible)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public FormModel Add(IFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (_fields.Any(existing => string.Equals(existing.Key, field.Key, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"A form field with key '{field.Key}' is already registered.");
        }

        _fields.Add(field);
        field.ValueChanged += OnFieldValueChanged;
        ReconcileActiveField();
        return this;
    }

    public void SetUpdateError(string? error)
        => UpdateError = string.IsNullOrWhiteSpace(error) ? null : error;

    private void OnFieldValueChanged(IFormField field)
    {
        UpdateError = null;
        Changed?.Invoke(this, field);
    }

    public IFormField? ActiveField
    {
        get
        {
            ReconcileActiveField();
            return _fields.Count == 0 ? null : _fields[_activeFieldIndex];
        }
    }

    public int GetVisiblePosition(int sourceIndex)
    {
        var visiblePosition = 0;
        for (var i = 0; i < _fields.Count; i++)
        {
            if (!_fields[i].IsVisible)
            {
                continue;
            }

            if (i == sourceIndex)
            {
                return visiblePosition;
            }

            visiblePosition++;
        }

        return -1;
    }

    public int GetSourceIndexAtVisiblePosition(int visiblePosition)
    {
        if (visiblePosition < 0)
        {
            return -1;
        }

        var current = 0;
        for (var i = 0; i < _fields.Count; i++)
        {
            if (!_fields[i].IsVisible)
            {
                continue;
            }

            if (current == visiblePosition)
            {
                return i;
            }

            current++;
        }

        return -1;
    }

    public void ReconcileActiveField()
    {
        if (_fields.Count == 0)
        {
            _activeFieldIndex = 0;
            return;
        }

        _activeFieldIndex = Math.Clamp(_activeFieldIndex, 0, _fields.Count - 1);
        if (_fields[_activeFieldIndex].IsVisible && _fields[_activeFieldIndex].IsEnabled)
        {
            return;
        }

        for (var distance = 1; distance < _fields.Count; distance++)
        {
            var forward = _activeFieldIndex + distance;
            if (forward < _fields.Count && _fields[forward].IsVisible && _fields[forward].IsEnabled)
            {
                _activeFieldIndex = forward;
                return;
            }

            var backward = _activeFieldIndex - distance;
            if (backward >= 0 && _fields[backward].IsVisible && _fields[backward].IsEnabled)
            {
                _activeFieldIndex = backward;
                return;
            }
        }
    }
}

public sealed class FormDefinition<TResult>
{
    private readonly Func<TResult> _buildResult;

    public FormDefinition(
        FormModel model,
        Func<TResult> buildResult,
        FormUpdateMode updateMode = FormUpdateMode.Staged,
        Func<TResult, CancellationToken, ValueTask>? updateAsync = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _buildResult = buildResult ?? throw new ArgumentNullException(nameof(buildResult));
        UpdateMode = updateMode;
        UpdateAsync = updateAsync;
        if (updateMode == FormUpdateMode.Live && updateAsync is null)
        {
            throw new ArgumentException("Live forms require an update callback.", nameof(updateAsync));
        }
    }

    public FormModel Model { get; }

    public FormUpdateMode UpdateMode { get; }

    public Func<TResult, CancellationToken, ValueTask>? UpdateAsync { get; }

    public TResult BuildResult() => _buildResult();
}

public enum FormUpdateMode
{
    Staged,
    Live
}

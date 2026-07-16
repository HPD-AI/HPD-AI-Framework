using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed record FormChoice<T>(
    string Key,
    T Value,
    string Title,
    string? Description = null,
    bool Disabled = false);

public enum FormChoicePresentation
{
    Auto,
    Inline,
    Picker
}

public sealed class ChoiceFormField<T> : FormField<T>
{
    private readonly IReadOnlyList<FormChoice<T>> _choices;
    private readonly T _initialValue;
    private readonly Func<ChoiceFormField<T>, ValueTask<FormChoice<T>?>>? _picker;
    private int _index;
    private int _isPicking;
    private Task _picking = Task.CompletedTask;

    public ChoiceFormField(
        string key,
        string label,
        IReadOnlyList<FormChoice<T>> choices,
        T value,
        string? description = null,
        FormChoicePresentation presentation = FormChoicePresentation.Auto,
        Func<ChoiceFormField<T>, ValueTask<FormChoice<T>?>>? picker = null)
        : base(key, label, description, isRequired: true)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0)
        {
            throw new ArgumentException("At least one choice is required.", nameof(choices));
        }

        if (choices.Select(static choice => choice.Key).Distinct(StringComparer.Ordinal).Count() != choices.Count)
        {
            throw new ArgumentException("Choice keys must be unique.", nameof(choices));
        }

        _choices = choices;
        _initialValue = value;
        _index = FindValue(value);
        if (_index < 0)
        {
            throw new ArgumentException("The initial value is not present in the choices.", nameof(value));
        }

        if (_choices[_index].Disabled)
        {
            throw new ArgumentException("The initial choice cannot be disabled.", nameof(value));
        }

        Presentation = presentation;
        _picker = picker;
        if (presentation == FormChoicePresentation.Picker && picker is null)
        {
            throw new ArgumentException("Picker presentation requires a picker callback.", nameof(picker));
        }
    }

    public IReadOnlyList<FormChoice<T>> Choices => _choices;

    public FormChoicePresentation Presentation { get; }

    public FormChoice<T> SelectedChoice => _choices[_index];

    public override string? Description
    {
        get => SelectedChoice.Description ?? base.Description;
        protected set => base.Description = value;
    }

    public override T Value => SelectedChoice.Value;

    public override string DisplayValue => SelectedChoice.Title;

    public override bool IsDirty => !EqualityComparer<T>.Default.Equals(Value, _initialValue);

    public override FormFieldInteraction Interaction
        => Presentation == FormChoicePresentation.Picker
            ? FormFieldInteraction.Activate
            : FormFieldInteraction.Change;

    public override PromptValidationResult Validate()
    {
        Error = null;
        return PromptValidationResult.Valid;
    }

    public override bool HandleInput(in KeyEvent input)
    {
        if (Presentation == FormChoicePresentation.Picker)
        {
            if (input.Key != KeyCode.Enter)
            {
                return false;
            }

            if (Interlocked.Exchange(ref _isPicking, 1) == 0)
            {
                _picking = PickAsync();
            }

            return true;
        }

        switch (input.Key)
        {
            case KeyCode.LeftArrow:
                Move(-1);
                return true;
            case KeyCode.RightArrow:
            case KeyCode.Character when input.Character.Value == ' ':
            case KeyCode.Enter:
                Move(1);
                return true;
            default:
                return false;
        }
    }

    public override ValueTask WaitForPendingChangeAsync()
        => new(_picking);

    public bool Select(T value)
    {
        var index = FindValue(value);
        if (index < 0 || _choices[index].Disabled)
        {
            return false;
        }

        if (_index == index)
        {
            return true;
        }

        _index = index;
        NotifyValueChanged();
        return true;
    }

    private int FindValue(T value)
    {
        for (var i = 0; i < _choices.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(_choices[i].Value, value))
            {
                return i;
            }
        }

        return -1;
    }

    private void Move(int delta)
    {
        for (var attempt = 1; attempt <= _choices.Count; attempt++)
        {
            var candidate = (_index + (delta * attempt)) % _choices.Count;
            if (candidate < 0)
            {
                candidate += _choices.Count;
            }

            if (!_choices[candidate].Disabled)
            {
                if (_index == candidate)
                {
                    return;
                }

                _index = candidate;
                NotifyValueChanged();
                return;
            }
        }
    }

    private async Task PickAsync()
    {
        try
        {
            var selected = await _picker!(this).ConfigureAwait(false);
            if (selected is not null)
            {
                Select(selected.Value);
            }

            Error = null;
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            Interlocked.Exchange(ref _isPicking, 0);
        }
    }
}

public sealed class MultiChoiceFormField<T> : FormField<IReadOnlyList<T>>
{
    private readonly IReadOnlyList<FormChoice<T>> _choices;
    private readonly HashSet<string> _selectedKeys;
    private readonly HashSet<string> _initialKeys;
    private int _index;

    public MultiChoiceFormField(
        string key,
        string label,
        IReadOnlyList<FormChoice<T>> choices,
        IEnumerable<string>? selectedKeys = null,
        string? description = null,
        int minimumSelected = 0,
        int? maximumSelected = null)
        : base(key, label, description, minimumSelected > 0)
    {
        _choices = choices ?? throw new ArgumentNullException(nameof(choices));
        if (_choices.Count == 0)
        {
            throw new ArgumentException("At least one choice is required.", nameof(choices));
        }

        MinimumSelected = minimumSelected;
        MaximumSelected = maximumSelected;
        _selectedKeys = new HashSet<string>(selectedKeys ?? [], StringComparer.Ordinal);
        _initialKeys = new HashSet<string>(_selectedKeys, StringComparer.Ordinal);
    }

    public int MinimumSelected { get; }

    public int? MaximumSelected { get; }

    public override IReadOnlyList<T> Value => _choices
        .Where(choice => _selectedKeys.Contains(choice.Key))
        .Select(static choice => choice.Value)
        .ToArray();

    public override string DisplayValue => _selectedKeys.Count == 0
        ? "None"
        : $"{_selectedKeys.Count} selected";

    public override bool IsDirty => !_initialKeys.SetEquals(_selectedKeys);

    public override FormFieldInteraction Interaction => FormFieldInteraction.Change;

    public override PromptValidationResult Validate()
    {
        if (_selectedKeys.Count < MinimumSelected)
        {
            Error = $"Select at least {MinimumSelected}.";
            return PromptValidationResult.Invalid(Error);
        }

        if (MaximumSelected is { } maximum && _selectedKeys.Count > maximum)
        {
            Error = $"Select no more than {maximum}.";
            return PromptValidationResult.Invalid(Error);
        }

        Error = null;
        return PromptValidationResult.Valid;
    }

    public override bool HandleInput(in KeyEvent input)
    {
        switch (input.Key)
        {
            case KeyCode.LeftArrow:
                Move(-1);
                return true;
            case KeyCode.RightArrow:
                Move(1);
                return true;
            case KeyCode.Character when input.Character.Value == ' ':
            case KeyCode.Enter:
                Toggle();
                return true;
            default:
                return false;
        }
    }

    private void Move(int delta)
    {
        for (var attempt = 1; attempt <= _choices.Count; attempt++)
        {
            var candidate = (_index + (delta * attempt)) % _choices.Count;
            if (candidate < 0)
            {
                candidate += _choices.Count;
            }

            if (!_choices[candidate].Disabled)
            {
                _index = candidate;
                return;
            }
        }
    }

    private void Toggle()
    {
        var choice = _choices[_index];
        if (choice.Disabled)
        {
            return;
        }

        var changed = _selectedKeys.Remove(choice.Key);
        if (!changed)
        {
            if (MaximumSelected is not { } maximum || _selectedKeys.Count < maximum)
            {
                changed = _selectedKeys.Add(choice.Key);
            }
        }

        if (changed)
        {
            NotifyValueChanged();
        }
    }
}

public static class FormFields
{
    public static ChoiceFormField<bool> Boolean(
        string key,
        string label,
        bool value = false,
        string? description = null)
        => new(
            key,
            label,
            [
                new FormChoice<bool>("false", false, "Off"),
                new FormChoice<bool>("true", true, "On")
            ],
            value,
            description,
            FormChoicePresentation.Inline);
}

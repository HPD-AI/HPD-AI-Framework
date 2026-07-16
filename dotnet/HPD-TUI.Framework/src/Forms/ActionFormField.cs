using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed class ActionFormField<T> : FormField<T>
{
    private readonly Func<T> _value;
    private readonly Func<string> _displayValue;
    private readonly Func<ValueTask> _activate;
    private readonly PromptValidator<T>? _validator;
    private readonly T _initialValue;
    private int _isActivating;
    private Task _activation = Task.CompletedTask;

    public ActionFormField(
        string key,
        string label,
        Func<T> value,
        Func<string> displayValue,
        Func<ValueTask> activate,
        string? description = null,
        PromptValidator<T>? validator = null)
        : base(key, label, description, isRequired: false)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
        _displayValue = displayValue ?? throw new ArgumentNullException(nameof(displayValue));
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        _validator = validator;
        _initialValue = _value();
    }

    public override T Value => _value();

    public override string DisplayValue => _displayValue();

    public override bool IsDirty => !EqualityComparer<T>.Default.Equals(Value, _initialValue);

    public override FormFieldInteraction Interaction => FormFieldInteraction.Activate;

    public override PromptValidationResult Validate()
    {
        var result = _validator?.Invoke(Value) ?? PromptValidationResult.Valid;
        Error = result.IsValid ? null : result.Message;
        return result;
    }

    public override bool HandleInput(in KeyEvent input)
    {
        if (input.Key != KeyCode.Enter)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _isActivating, 1) == 0)
        {
            _activation = ActivateAsync();
        }

        return true;
    }

    public override ValueTask WaitForPendingChangeAsync()
        => new(_activation);

    private async Task ActivateAsync()
    {
        var before = Value;
        try
        {
            await _activate().ConfigureAwait(false);
            Error = null;
            if (!EqualityComparer<T>.Default.Equals(before, Value))
            {
                NotifyValueChanged();
            }
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            Interlocked.Exchange(ref _isActivating, 0);
        }
    }
}

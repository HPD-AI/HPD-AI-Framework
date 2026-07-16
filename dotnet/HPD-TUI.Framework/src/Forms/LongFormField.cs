using System.Globalization;
using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed class LongFormField : FormField<long?>
{
    private readonly long? _initialValue;
    private readonly TextFormField _editor;
    private long? _value;

    public LongFormField(
        string key,
        string label,
        long? value = null,
        string? description = null,
        bool isRequired = false,
        long? minimum = null,
        long? maximum = null,
        long step = 1,
        NumericFieldInteraction interaction = NumericFieldInteraction.StepperAndInput)
        : base(key, label, description, isRequired)
    {
        if (minimum is { } min && maximum is { } max && min > max)
        {
            throw new ArgumentException("Minimum cannot be greater than maximum.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        _value = value;
        _initialValue = value;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        InteractionMode = interaction;
        _editor = new TextFormField(
            $"{key}.editor",
            label,
            value?.ToString(CultureInfo.InvariantCulture),
            description,
            isRequired,
            validator: ValidateText);

        if (!Validate().IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public long? Minimum { get; }

    public long? Maximum { get; }

    public long Step { get; }

    public NumericFieldInteraction InteractionMode { get; }

    public override long? Value => IsEditing ? Parse(_editor.Value) : _value;

    public override string DisplayValue => IsEditing
        ? _editor.DisplayValue
        : _value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public override bool IsDirty => Value != _initialValue;

    public override FormFieldInteraction Interaction => InteractionMode switch
    {
        NumericFieldInteraction.Text => FormFieldInteraction.Edit,
        NumericFieldInteraction.Stepper => FormFieldInteraction.Change,
        _ => FormFieldInteraction.Change | FormFieldInteraction.Edit
    };

    public override PromptValidationResult Validate()
    {
        var result = ValidateText(IsEditing ? _editor.Value : DisplayValue);
        Error = result.IsValid ? null : result.Message;
        return result;
    }

    public override bool BeginEdit()
    {
        if (IsEditing || InteractionMode == NumericFieldInteraction.Stepper)
        {
            return false;
        }

        _editor.ResetValue(_value?.ToString(CultureInfo.InvariantCulture));
        IsEditing = true;
        _editor.BeginEdit();
        return true;
    }

    public override bool AcceptEdit()
    {
        if (!IsEditing || !Validate().IsValid)
        {
            return IsEditing;
        }

        var previous = _value;
        _value = Parse(_editor.Value);
        _editor.AcceptEdit();
        IsEditing = false;
        if (_value != previous)
        {
            NotifyValueChanged();
        }

        return true;
    }

    public override bool CancelEdit()
    {
        if (!IsEditing)
        {
            return false;
        }

        _editor.CancelEdit();
        IsEditing = false;
        Error = null;
        return true;
    }

    public override bool HandleInput(in KeyEvent input)
    {
        if (IsEditing)
        {
            return _editor.HandleInput(in input);
        }

        if (InteractionMode == NumericFieldInteraction.Text)
        {
            return false;
        }

        if (input.Key == KeyCode.LeftArrow)
        {
            StepBy(-Step);
            return true;
        }

        if (input.Key == KeyCode.RightArrow)
        {
            StepBy(Step);
            return true;
        }

        return false;
    }

    private PromptValidationResult ValidateText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return IsRequired
                ? PromptValidationResult.Invalid("Required.")
                : PromptValidationResult.Valid;
        }

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return PromptValidationResult.Invalid("Must be an integer.");
        }

        if (Minimum is { } min && parsed < min)
        {
            return PromptValidationResult.Invalid($"Must be at least {min}.");
        }

        return Maximum is { } max && parsed > max
            ? PromptValidationResult.Invalid($"Must be no more than {max}.")
            : PromptValidationResult.Valid;
    }

    private static long? Parse(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private void StepBy(long delta)
    {
        var previous = _value;
        var origin = _value ?? Minimum ?? 0;
        long next;
        try
        {
            next = checked(origin + delta);
        }
        catch (OverflowException)
        {
            next = delta > 0 ? long.MaxValue : long.MinValue;
        }

        if (Minimum is { } min)
        {
            next = Math.Max(next, min);
        }

        if (Maximum is { } max)
        {
            next = Math.Min(next, max);
        }

        _value = next;
        Error = null;
        if (_value != previous)
        {
            NotifyValueChanged();
        }
    }
}

using System.Globalization;
using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public enum NumericFieldInteraction
{
    Text,
    Stepper,
    StepperAndInput
}

public sealed class IntegerFormField : FormField<int?>
{
    private readonly int? _initialValue;
    private readonly TextFormField _editor;
    private int? _value;

    public IntegerFormField(
        string key,
        string label,
        int? value = null,
        string? description = null,
        bool isRequired = false,
        int? minimum = null,
        int? maximum = null,
        int step = 1,
        NumericFieldInteraction interaction = NumericFieldInteraction.StepperAndInput)
        : base(key, label, description, isRequired)
    {
        if (minimum is { } min && maximum is { } max && min > max)
        {
            throw new ArgumentException("Minimum cannot be greater than maximum.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        if (value is { } initial && (minimum is { } lower && initial < lower || maximum is { } upper && initial > upper))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        _value = value;
        _initialValue = value;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        InteractionMode = interaction;
        _editor = CreateEditor(value);
    }

    public int? Minimum { get; }

    public int? Maximum { get; }

    public int Step { get; }

    public NumericFieldInteraction InteractionMode { get; }

    public override int? Value => IsEditing ? Parse(_editor.Value) : _value;

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
        var parsed = Value;
        if (IsRequired && parsed is null)
        {
            Error = "Required.";
            return PromptValidationResult.Invalid(Error);
        }

        if (parsed is { } value && Minimum is { } min && value < min)
        {
            Error = $"Must be at least {min}.";
            return PromptValidationResult.Invalid(Error);
        }

        if (parsed is { } bounded && Maximum is { } max && bounded > max)
        {
            Error = $"Must be no more than {max}.";
            return PromptValidationResult.Invalid(Error);
        }

        if (IsEditing && _editor.Value.Length > 0 && parsed is null)
        {
            Error = "Must be an integer.";
            return PromptValidationResult.Invalid(Error);
        }

        Error = null;
        return PromptValidationResult.Valid;
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

        switch (input.Key)
        {
            case KeyCode.LeftArrow:
                StepBy(-Step);
                return true;
            case KeyCode.RightArrow:
                StepBy(Step);
                return true;
            default:
                return false;
        }
    }

    private TextFormField CreateEditor(int? value)
        => new(
            $"{Key}.editor",
            Label,
            value?.ToString(CultureInfo.InvariantCulture),
            Description,
            IsRequired,
            validator: text =>
            {
                if (string.IsNullOrEmpty(text))
                {
                    return IsRequired
                        ? PromptValidationResult.Invalid("Required.")
                        : PromptValidationResult.Valid;
                }

                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
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
            });

    private static int? Parse(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private void StepBy(int delta)
    {
        var previous = _value;
        var origin = _value ?? Minimum ?? 0;
        var next = (long)origin + delta;
        if (Minimum is { } min)
        {
            next = Math.Max(next, min);
        }

        if (Maximum is { } max)
        {
            next = Math.Min(next, max);
        }

        _value = (int)Math.Clamp(next, int.MinValue, int.MaxValue);
        Error = null;
        if (_value != previous)
        {
            NotifyValueChanged();
        }
    }
}

using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed class TextFormField : FormField<string>
{
    private readonly StringBuilder _text = new();
    private readonly PromptValidator<string>? _validator;
    private readonly string _initialValue;
    private string _editSnapshot = string.Empty;

    public TextFormField(
        string key,
        string label,
        string? value = null,
        string? description = null,
        bool isRequired = false,
        bool isSecret = false,
        bool isMultiline = false,
        PromptValidator<string>? validator = null)
        : base(key, label, description, isRequired)
    {
        IsSecret = isSecret;
        IsMultiline = isMultiline;
        _validator = validator;
        _initialValue = value ?? string.Empty;
        _text.Append(_initialValue);
        Cursor = _text.Length;
    }

    public bool IsSecret { get; }

    public bool IsMultiline { get; }

    public override bool IsDirty => !StringComparer.Ordinal.Equals(Value, _initialValue);

    public override bool IsEditing { get; protected set; }

    public override FormFieldInteraction Interaction => FormFieldInteraction.Edit;

    public int Cursor { get; private set; }

    public override string Value => _text.ToString();

    public override string DisplayValue => IsSecret ? new string('*', _text.Length) : Value;

    public override PromptValidationResult Validate()
    {
        if (IsRequired && _text.Length == 0)
        {
            Error = "Required.";
            return PromptValidationResult.Invalid(Error);
        }

        var result = _validator?.Invoke(Value) ?? PromptValidationResult.Valid;
        Error = result.IsValid ? null : result.Message;
        return result;
    }

    public override bool BeginEdit()
    {
        if (IsEditing)
        {
            return false;
        }

        _editSnapshot = Value;
        IsEditing = true;
        Cursor = _text.Length;
        return true;
    }

    internal void ResetValue(string? value)
    {
        if (IsEditing)
        {
            throw new InvalidOperationException("Cannot reset a text field while it is being edited.");
        }

        _text.Clear();
        _text.Append(value ?? string.Empty);
        Cursor = _text.Length;
        Error = null;
    }

    public override bool AcceptEdit()
    {
        if (!IsEditing)
        {
            return false;
        }

        if (!Validate().IsValid)
        {
            return true;
        }

        IsEditing = false;
        if (!StringComparer.Ordinal.Equals(Value, _editSnapshot))
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

        _text.Clear();
        _text.Append(_editSnapshot);
        Cursor = _text.Length;
        Error = null;
        IsEditing = false;
        return true;
    }

    public override bool HandleInput(in KeyEvent input)
    {
        if (!IsEditing)
        {
            return false;
        }

        switch (input.Key)
        {
            case KeyCode.Character:
                Insert(input.Character);
                return true;
            case KeyCode.Paste when !string.IsNullOrEmpty(input.Text):
                _text.Insert(Cursor, input.Text);
                Cursor += input.Text.Length;
                return true;
            case KeyCode.Backspace when Cursor > 0:
                _text.Remove(Cursor - 1, 1);
                Cursor--;
                return true;
            case KeyCode.Delete when Cursor < _text.Length:
                _text.Remove(Cursor, 1);
                return true;
            case KeyCode.LeftArrow when Cursor > 0:
                Cursor--;
                return true;
            case KeyCode.RightArrow when Cursor < _text.Length:
                Cursor++;
                return true;
            case KeyCode.Home:
                Cursor = 0;
                return true;
            case KeyCode.End:
                Cursor = _text.Length;
                return true;
            case KeyCode.Enter when IsMultiline && input.Modifiers.HasFlag(KeyModifiers.Shift):
                Insert(new Rune('\n'));
                return true;
            default:
                return false;
        }
    }

    private void Insert(Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        if (!rune.TryEncodeToUtf16(buffer, out var written))
        {
            return;
        }

        _text.Insert(Cursor, buffer[..written]);
        Cursor += written;
    }
}

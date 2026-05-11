using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed class TextFormField : IFormField
{
    private readonly StringBuilder _text = new();
    private readonly PromptValidator<string>? _validator;
    private readonly string _initialValue;

    public TextFormField(
        string key,
        string label,
        string? value = null,
        string? help = null,
        bool isRequired = false,
        bool isSecret = false,
        bool isMultiline = false,
        PromptValidator<string>? validator = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Key is required.", nameof(key)) : key;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Help = help;
        IsRequired = isRequired;
        IsSecret = isSecret;
        IsMultiline = isMultiline;
        _validator = validator;
        _initialValue = value ?? string.Empty;
        _text.Append(_initialValue);
        Cursor = _text.Length;
    }

    public string Key { get; }

    public string Label { get; }

    public string? Help { get; }

    public string? Error { get; private set; }

    public bool IsRequired { get; }

    public bool IsSecret { get; }

    public bool IsMultiline { get; }

    public bool IsDirty => !StringComparer.Ordinal.Equals(Value, _initialValue);

    public int Cursor { get; private set; }

    public string Value => _text.ToString();

    public string DisplayValue => IsSecret ? new string('*', _text.Length) : Value;

    public PromptValidationResult Validate()
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

    public bool HandleInput(in KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.Character:
                Insert(key.Character);
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
            case KeyCode.Enter when IsMultiline && key.Modifiers.HasFlag(KeyModifiers.Shift):
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

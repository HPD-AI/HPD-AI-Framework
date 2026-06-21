using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed class EnumFormField<TEnum> : IFormField
    where TEnum : struct, Enum
{
    private readonly TEnum[] _values;
    private readonly TEnum _initialValue;
    private int _index;

    public EnumFormField(string key, string label, TEnum value = default, string? help = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Key is required.", nameof(key)) : key;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Help = help;
        _values = Enum.GetValues<TEnum>();
        _initialValue = value;
        _index = Array.IndexOf(_values, value);
        if (_index < 0)
        {
            _index = 0;
        }
    }

    public string Key { get; }

    public string Label { get; }

    public string? Help { get; }

    public string? Error => null;

    public bool IsRequired => true;

    public bool IsDirty => !EqualityComparer<TEnum>.Default.Equals(Value, _initialValue);

    public TEnum Value => _values.Length == 0 ? default : _values[_index];

    public string DisplayValue => Value.ToString();

    public PromptValidationResult Validate() => PromptValidationResult.Valid;

    public bool HandleInput(in KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.LeftArrow:
                Move(-1);
                return true;
            case KeyCode.RightArrow:
            case KeyCode.Character when key.Character.Value == ' ':
            case KeyCode.Enter:
                Move(1);
                return true;
            default:
                return false;
        }
    }

    private void Move(int delta)
    {
        if (_values.Length == 0)
        {
            _index = 0;
            return;
        }

        _index = (_index + delta) % _values.Length;
        if (_index < 0)
        {
            _index += _values.Length;
        }
    }
}

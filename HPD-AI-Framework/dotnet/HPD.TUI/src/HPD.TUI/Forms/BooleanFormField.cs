using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed class BooleanFormField : IFormField
{
    private readonly bool _initialValue;

    public BooleanFormField(string key, string label, bool value = false, string? help = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Key is required.", nameof(key)) : key;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Value = value;
        _initialValue = value;
        Help = help;
    }

    public string Key { get; }

    public string Label { get; }

    public string? Help { get; }

    public string? Error => null;

    public bool IsRequired => false;

    public bool IsDirty => Value != _initialValue;

    public bool Value { get; private set; }

    public string DisplayValue => Value ? "true" : "false";

    public PromptValidationResult Validate() => PromptValidationResult.Valid;

    public bool HandleInput(in KeyEvent key)
    {
        if ((key.Key is KeyCode.Character && key.Character.Value == ' ') || key.Key == KeyCode.Enter)
        {
            Value = !Value;
            return true;
        }

        return false;
    }
}

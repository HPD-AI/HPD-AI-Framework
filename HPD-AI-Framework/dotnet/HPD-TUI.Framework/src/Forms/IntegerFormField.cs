using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed class IntegerFormField : IFormField
{
    private readonly TextFormField _text;

    public IntegerFormField(string key, string label, int? value = null, string? help = null, bool isRequired = false)
    {
        _text = new TextFormField(
            key,
            label,
            value?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            help,
            isRequired,
            validator: ValidateInteger);
    }

    public string Key => _text.Key;

    public string Label => _text.Label;

    public string? Help => _text.Help;

    public string? Error => _text.Error;

    public bool IsRequired => _text.IsRequired;

    public bool IsDirty => _text.IsDirty;

    public int? Value => int.TryParse(_text.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
        ? value
        : null;

    public string DisplayValue => _text.DisplayValue;

    public PromptValidationResult Validate() => _text.Validate();

    public bool HandleInput(in KeyEvent key) => _text.HandleInput(in key);

    private static PromptValidationResult ValidateInteger(string value)
    {
        return string.IsNullOrEmpty(value) || int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)
            ? PromptValidationResult.Valid
            : PromptValidationResult.Invalid("Must be an integer.");
    }
}

using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public interface IFormField
{
    string Key { get; }

    string Label { get; }

    string? Help { get; }

    string? Error { get; }

    bool IsRequired { get; }

    bool IsDirty { get; }

    string DisplayValue { get; }

    PromptValidationResult Validate();

    bool HandleInput(in KeyEvent key);
}

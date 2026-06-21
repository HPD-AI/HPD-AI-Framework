using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

public sealed class FormController
{
    private readonly FormModel _model;

    public FormController(FormModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public Action<FormModel>? Submitted { get; set; }

    public Action? Canceled { get; set; }

    public bool HandleInput(in KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.UpArrow:
                Move(-1);
                return true;
            case KeyCode.DownArrow:
            case KeyCode.Tab:
                Move(1);
                return true;
            case KeyCode.Enter when key.Modifiers.HasFlag(KeyModifiers.Ctrl):
                Submit();
                return true;
            case KeyCode.Escape:
                Canceled?.Invoke();
                return true;
            default:
                return _model.ActiveField?.HandleInput(in key) == true;
        }
    }

    public void Move(int delta)
    {
        if (_model.Fields.Count == 0 || delta == 0)
        {
            return;
        }

        _model.ActiveFieldIndex = Math.Clamp(_model.ActiveFieldIndex + delta, 0, _model.Fields.Count - 1);
    }

    public PromptValidationResult Validate()
    {
        foreach (var field in _model.Fields)
        {
            var result = field.Validate();
            if (!result.IsValid)
            {
                return result;
            }
        }

        return PromptValidationResult.Valid;
    }

    public bool Submit()
    {
        var result = Validate();
        if (!result.IsValid)
        {
            return false;
        }

        Submitted?.Invoke(_model);
        return true;
    }
}

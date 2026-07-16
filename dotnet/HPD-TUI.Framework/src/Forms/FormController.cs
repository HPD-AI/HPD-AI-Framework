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

    public bool HandleInput(in KeyEvent input)
    {
        var active = _model.ActiveField;
        if (active?.IsEditing == true)
        {
            if (input.Key == KeyCode.Escape)
            {
                active.CancelEdit();
                return true;
            }

            if (input.Key == KeyCode.Enter && !input.Modifiers.HasFlag(KeyModifiers.Shift))
            {
                active.AcceptEdit();
                return true;
            }

            return active.HandleInput(in input);
        }

        switch (input.Key)
        {
            case KeyCode.UpArrow:
                Move(-1);
                return true;
            case KeyCode.DownArrow:
            case KeyCode.Tab when !input.Modifiers.HasFlag(KeyModifiers.Shift):
                Move(1);
                return true;
            case KeyCode.Tab:
                Move(-1);
                return true;
            case KeyCode.Home:
                MoveToBoundary(first: true);
                return true;
            case KeyCode.End:
                MoveToBoundary(first: false);
                return true;
            case KeyCode.Enter when input.Modifiers.HasFlag(KeyModifiers.Ctrl):
                Submit();
                return true;
            case KeyCode.Enter when active is not null && active.BeginEdit():
                return true;
            case KeyCode.Escape:
                Canceled?.Invoke();
                return true;
            default:
                return active?.HandleInput(in input) == true;
        }
    }

    public void Move(int delta)
    {
        if (_model.Fields.Count == 0 || delta == 0)
        {
            return;
        }

        var index = _model.ActiveFieldIndex;
        var direction = Math.Sign(delta);
        var remaining = Math.Abs(delta);
        while (remaining > 0)
        {
            var next = FindEnabled(index, direction);
            if (next < 0)
            {
                break;
            }

            index = next;
            remaining--;
        }

        _model.ActiveFieldIndex = index;
    }

    public PromptValidationResult Validate()
    {
        for (var i = 0; i < _model.Fields.Count; i++)
        {
            var field = _model.Fields[i];
            if (!field.IsVisible)
            {
                continue;
            }

            var result = field.Validate();
            if (!result.IsValid)
            {
                if (field.IsEnabled)
                {
                    _model.ActiveFieldIndex = i;
                }

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

    private int FindEnabled(int start, int direction)
    {
        for (var index = start + direction;
             index >= 0 && index < _model.Fields.Count;
             index += direction)
        {
            var field = _model.Fields[index];
            if (field.IsVisible && field.IsEnabled)
            {
                return index;
            }
        }

        return -1;
    }

    private void MoveToBoundary(bool first)
    {
        var indexes = first
            ? Enumerable.Range(0, _model.Fields.Count)
            : Enumerable.Range(0, _model.Fields.Count).Reverse();
        foreach (var index in indexes)
        {
            var field = _model.Fields[index];
            if (field.IsVisible && field.IsEnabled)
            {
                _model.ActiveFieldIndex = index;
                return;
            }
        }
    }
}

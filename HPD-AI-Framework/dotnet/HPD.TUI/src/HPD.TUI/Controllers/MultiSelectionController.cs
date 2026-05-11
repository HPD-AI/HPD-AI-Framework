using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Controllers;

public sealed class MultiSelectionController<T>
{
    private readonly MultiSelectionModel<T> _model;
    private readonly CollectionNavigationController<T> _navigation;

    public MultiSelectionController(MultiSelectionModel<T> model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _navigation = new CollectionNavigationController<T>(_model);
    }

    public int SelectedIndex => _navigation.ActiveIndex;

    public CollectionNavigationController<T> Navigation => _navigation;

    public ViewportModel Viewport => _navigation.Viewport;

    public Action<IReadOnlyList<T>>? Submitted { get; set; }

    public Action? Canceled { get; set; }

    public Func<bool>? CanSubmit { get; set; }

    public bool HandleInput(in KeyEvent key)
    {
        if (HandleFilterInput(in key))
        {
            return true;
        }

        switch (key.Key)
        {
            case KeyCode.UpArrow:
                Move(-1);
                return true;
            case KeyCode.DownArrow:
                Move(1);
                return true;
            case KeyCode.Home:
                _navigation.MoveFirst();
                return true;
            case KeyCode.End:
                _navigation.MoveLast();
                return true;
            case KeyCode.PageUp:
                _navigation.Page(_navigation.Viewport.WindowSize, -1);
                return true;
            case KeyCode.PageDown:
                _navigation.Page(_navigation.Viewport.WindowSize, 1);
                return true;
            case KeyCode.Character when key.Character.Value == ' ':
                _model.Toggle(_navigation.ActiveIndex);
                return true;
            case KeyCode.Enter:
                if (CanSubmit?.Invoke() == false)
                {
                    return true;
                }

                Submitted?.Invoke(_model.GetSelectedValues());
                return true;
            case KeyCode.Escape:
                Canceled?.Invoke();
                return true;
            default:
                return false;
        }
    }

    public void Move(int delta)
    {
        _navigation.Move(delta);
    }

    private bool HandleFilterInput(in KeyEvent key)
    {
        if (!_model.AllowFilter)
        {
            return false;
        }

        switch (key.Key)
        {
            case KeyCode.Character when key.Character.Value != ' ':
                _model.AppendQuery(key.Character);
                _navigation.MoveFirst();
                return true;
            case KeyCode.Backspace:
                if (_model.BackspaceQuery())
                {
                    _navigation.MoveFirst();
                    return true;
                }

                return false;
            default:
                return false;
        }
    }
}

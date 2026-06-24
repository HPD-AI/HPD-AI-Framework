using HPD.TUI.Core;

namespace HPD.TUI.Controllers;

public sealed class FocusManager
{
    private readonly Stack<IComponent?> _stack = new();
    private IComponent? _focused;

    public IComponent? Focused => _focused;

    public void SetFocus(IComponent? component)
    {
        if (ReferenceEquals(_focused, component))
        {
            return;
        }

        SetFocusedCore(component);
    }

    public void PushFocus(IComponent? component)
    {
        _stack.Push(_focused);
        SetFocusedCore(component);
    }

    public bool PopFocus()
    {
        if (_stack.Count == 0)
        {
            return false;
        }

        SetFocusedCore(_stack.Pop());
        return true;
    }

    public void Clear()
    {
        _stack.Clear();
        SetFocusedCore(null);
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        if (_focused is null)
        {
            return false;
        }

        return _focused.HandleInput(in key);
    }

    private void SetFocusedCore(IComponent? component)
    {
        if (_focused is IFocusable oldFocus)
        {
            oldFocus.IsFocused = false;
        }

        _focused = component;

        if (_focused is IFocusable newFocus)
        {
            newFocus.IsFocused = true;
        }
    }
}

using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Components;

public sealed class Viewport : Component
{
    public override ComponentDependencies Dependencies => ComponentDependencies.Static;
    private readonly List<string> _lines = [];
    private int _height;
    private int _scrollOffset;

    public Viewport(int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _height = height;
    }

    /// <summary>Gets or sets the number of visible rows.</summary>
    public int Height
    {
        get => _height;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            if (SetLayout(ref _height, value)) SetScrollOffset(_scrollOffset);
        }
    }

    /// <summary>Gets the zero-based first visible line.</summary>
    public int ScrollOffset => _scrollOffset;

    public int Count => _lines.Count;

    public void AddLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        InvalidateLayout();
        _lines.Add(line);
    }

    public void ScrollBy(int delta)
    {
        var max = Math.Max(0, _lines.Count - Height);
        SetScrollOffset(Math.Clamp(_scrollOffset + delta, 0, max));
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        var width = 0;
        foreach (var line in _lines)
        {
            width = Math.Max(width, Math.Min(maxWidth, UnicodeWidth.GetWidth(line)));
        }

        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        var visible = Math.Min(Height, Math.Max(0, _lines.Count - ScrollOffset));

        for (var i = 0; i < visible; i++)
        {
            output.Write(_lines[ScrollOffset + i].AsSpan(), context.Theme.Text);
            if (i < Height - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.UpArrow:
                ScrollBy(-1);
                return true;
            case KeyCode.DownArrow:
                ScrollBy(1);
                return true;
            case KeyCode.PageUp:
                ScrollBy(-Height);
                return true;
            case KeyCode.PageDown:
                ScrollBy(Height);
                return true;
            case KeyCode.Home:
                SetScrollOffset(0);
                return true;
            case KeyCode.End:
                SetScrollOffset(Math.Max(0, _lines.Count - Height));
                return true;
            default:
                return false;
        }
    }

    private void SetScrollOffset(int value) => SetPaint(ref _scrollOffset, value);
}

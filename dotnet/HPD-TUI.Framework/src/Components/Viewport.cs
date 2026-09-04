using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Components;

public sealed class Viewport : Component
{
    public override ComponentDependencies Dependencies => ComponentDependencies.Static;
    private readonly List<string> _lines = [];

    public Viewport(int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Height = height;
    }

    public int Height { get; set; }

    public int ScrollOffset { get; private set; }

    public int Count => _lines.Count;

    public void AddLine(string line)
    {
        _lines.Add(line ?? throw new ArgumentNullException(nameof(line)));
    }

    public void ScrollBy(int delta)
    {
        var max = Math.Max(0, _lines.Count - Height);
        ScrollOffset = Math.Clamp(ScrollOffset + delta, 0, max);
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
                ScrollOffset = 0;
                return true;
            case KeyCode.End:
                ScrollOffset = Math.Max(0, _lines.Count - Height);
                return true;
            default:
                return false;
        }
    }
}

using HPD.TUI.Core;

namespace HPD.TUI.Layout;

public sealed class Stack : Component
{
    private readonly List<IComponent> _children = [];

    public Stack(Orientation orientation = Orientation.Vertical)
    {
        Orientation = orientation;
    }

    public Orientation Orientation { get; }

    public int Gap { get; init; }

    public IReadOnlyList<IComponent> Children => _children;

    public override ComponentDependencies Dependencies => ComponentDependencies.Static;

    public Stack Add(IComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        AdoptChild(child);
        _children.Add(child);
        InvalidateLayout();
        return this;
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        if (_children.Count == 0)
        {
            return new Measurement(0, 0);
        }

        return Orientation == Orientation.Vertical
            ? MeasureVertical(in context, maxWidth)
            : MeasureHorizontal(in context, maxWidth);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        if (Orientation == Orientation.Vertical)
        {
            RenderVertical(in context, maxWidth, ref output);
            return;
        }

        RenderHorizontal(in context, maxWidth, ref output);
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        foreach (var child in _children)
        {
            if (child.HandleInput(in key))
            {
                return true;
            }
        }

        return false;
    }

    private Measurement MeasureVertical(in RenderContext context, int maxWidth)
    {
        var min = 0;
        var max = 0;
        var height = 0;
        foreach (var child in _children)
        {
            var measurement = MeasureChild(child, in context, maxWidth);
            min = Math.Max(min, measurement.MinWidth);
            max = Math.Max(max, measurement.MaxWidth);
            height += measurement.Height;
        }

        height += Math.Max(0, _children.Count - 1) * (Gap + 1);
        return new Measurement(Math.Min(min, maxWidth), Math.Min(max, maxWidth), height);
    }

    private Measurement MeasureHorizontal(in RenderContext context, int maxWidth)
    {
        var min = Math.Max(0, (_children.Count - 1) * Gap);
        var max = min;
        var height = 0;
        foreach (var child in _children)
        {
            var measurement = MeasureChild(child, in context, maxWidth);
            min += measurement.MinWidth;
            max += measurement.MaxWidth;
            height = Math.Max(height, measurement.Height);
        }

        return new Measurement(Math.Min(min, maxWidth), Math.Min(max, maxWidth), height);
    }

    private void RenderVertical(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        for (var i = 0; i < _children.Count; i++)
        {
            output.Render(_children[i], in context, maxWidth);

            if (i < _children.Count - 1)
            {
                for (var gap = 0; gap <= Gap; gap++)
                {
                    output.WriteLineBreak();
                }
            }
        }
    }

    private void RenderHorizontal(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        Span<char> gapBuffer = Gap > 0 ? stackalloc char[Math.Min(Gap, 256)] : [];
        if (gapBuffer.Length > 0)
        {
            gapBuffer.Fill(' ');
        }

        var remainingWidth = maxWidth;
        for (var i = 0; i < _children.Count; i++)
        {
            if (remainingWidth <= 0)
            {
                break;
            }

            var measurement = MeasureChild(_children[i], in context, remainingWidth);
            var allocatedWidth = Math.Clamp(measurement.MaxWidth, 0, remainingWidth);
            if (allocatedWidth <= 0)
            {
                continue;
            }

            output.Render(_children[i], in context, allocatedWidth);
            remainingWidth -= allocatedWidth;

            if (i < _children.Count - 1 && Gap > 0)
            {
                var gapRemaining = Math.Min(Gap, remainingWidth);
                while (gapRemaining > 0)
                {
                    var count = Math.Min(gapRemaining, gapBuffer.Length);
                    output.Write(gapBuffer[..count], context.Theme.Text);
                    gapRemaining -= count;
                    remainingWidth -= count;
                }
            }
        }
    }
}

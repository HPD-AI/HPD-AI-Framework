using HPD.TUI.Core;

namespace HPD.TUI.Layout;

public sealed class Stack : IComponent
{
    private readonly List<IComponent> _children = [];

    public Stack(Orientation orientation = Orientation.Vertical)
    {
        Orientation = orientation;
    }

    public Orientation Orientation { get; }

    public int Gap { get; init; }

    public IReadOnlyList<IComponent> Children => _children;

    public Stack Add(IComponent child)
    {
        _children.Add(child ?? throw new ArgumentNullException(nameof(child)));
        return this;
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        if (_children.Count == 0)
        {
            return new Measurement(0, 0);
        }

        return Orientation == Orientation.Vertical
            ? MeasureVertical(in context, maxWidth)
            : MeasureHorizontal(in context, maxWidth);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (Orientation == Orientation.Vertical)
        {
            RenderVertical(in context, maxWidth, ref output);
            return;
        }

        RenderHorizontal(in context, maxWidth, ref output);
    }

    public void HandleInput(in KeyEvent key)
    {
        foreach (var child in _children)
        {
            child.HandleInput(in key);
        }
    }

    public void Invalidate()
    {
        foreach (var child in _children)
        {
            child.Invalidate();
        }
    }

    private Measurement MeasureVertical(in RenderContext context, int maxWidth)
    {
        var min = 0;
        var max = 0;
        var height = 0;
        foreach (var child in _children)
        {
            var measurement = child.Measure(in context, maxWidth);
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
            var measurement = child.Measure(in context, maxWidth);
            min += measurement.MinWidth;
            max += measurement.MaxWidth;
            height = Math.Max(height, measurement.Height);
        }

        return new Measurement(Math.Min(min, maxWidth), Math.Min(max, maxWidth), height);
    }

    private void RenderVertical(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        for (var i = 0; i < _children.Count; i++)
        {
            _children[i].Render(in context, maxWidth, ref output);

            if (i < _children.Count - 1)
            {
                for (var gap = 0; gap <= Gap; gap++)
                {
                    output.WriteLineBreak();
                }
            }
        }
    }

    private void RenderHorizontal(in RenderContext context, int maxWidth, ref SegmentWriter output)
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

            var measurement = _children[i].Measure(in context, remainingWidth);
            var allocatedWidth = Math.Clamp(measurement.MaxWidth, 0, remainingWidth);
            if (allocatedWidth <= 0)
            {
                continue;
            }

            _children[i].Render(in context, allocatedWidth, ref output);
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

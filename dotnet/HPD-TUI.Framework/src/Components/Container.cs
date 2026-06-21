using HPD.TUI.Core;

namespace HPD.TUI.Components;

public class Container : IComponent
{
    private readonly List<IComponent> _children = [];

    public IReadOnlyList<IComponent> Children => _children;

    public void Add(IComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }

    public bool Remove(IComponent child) => _children.Remove(child);

    public void Clear() => _children.Clear();

    public virtual Measurement Measure(in RenderContext context, int maxWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxWidth);

        var minWidth = 0;
        var desiredWidth = 0;

        foreach (var child in _children)
        {
            var measurement = child.Measure(in context, maxWidth);
            minWidth = Math.Max(minWidth, measurement.MinWidth);
            desiredWidth = Math.Max(desiredWidth, measurement.MaxWidth);
        }

        return new Measurement(minWidth, Math.Min(maxWidth, desiredWidth));
    }

    public virtual void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        for (var i = 0; i < _children.Count; i++)
        {
            _children[i].Render(in context, maxWidth, ref output);

            if (i < _children.Count - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public virtual void HandleInput(in KeyEvent key)
    {
    }

    public virtual void Invalidate()
    {
        foreach (var child in _children)
        {
            child.Invalidate();
        }
    }
}

using HPD.TUI.Core;

namespace HPD.TUI.Components;

public class Container : Component
{
    private readonly List<IComponent> _children = [];

    public IReadOnlyList<IComponent> Children => _children;

    public void Add(IComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Lifecycle.Adopt(((IComponent)this).Lifecycle.Id);
        _children.Add(child);
        InvalidateLayout();
    }

    public bool Remove(IComponent child)
    {
        if (!_children.Remove(child)) return false;
        child.Lifecycle.Release(((IComponent)this).Lifecycle.Id);
        InvalidateLayout();
        return true;
    }

    public void Clear()
    {
        var parent = ((IComponent)this).Lifecycle.Id;
        foreach (var child in _children) child.Lifecycle.Release(parent);
        if (_children.Count == 0) return;
        _children.Clear();
        InvalidateLayout();
    }

    public override Measurement Measure(in RenderContext context, int maxWidth)
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

    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
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
}

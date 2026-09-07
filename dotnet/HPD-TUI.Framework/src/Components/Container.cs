using HPD.TUI.Core;

namespace HPD.TUI.Components;

public class Container : Component
{
    private readonly List<IComponent> _children = [];

    public IReadOnlyList<IComponent> Children => _children;

    public override ComponentDependencies Dependencies => ComponentDependencies.Static;

    public void Add(IComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        AdoptChild(child);
        _children.Add(child);
        InvalidateLayout();
    }

    public bool Remove(IComponent child)
    {
        if (!_children.Contains(child)) return false;
        ReleaseChild(child);
        _children.Remove(child);
        InvalidateLayout();
        return true;
    }

    public void Clear()
    {
        if (_children.Count == 0) return;
        foreach (var child in _children.ToArray()) ReleaseChild(child);
        _children.Clear();
        InvalidateLayout();
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        ArgumentOutOfRangeException.ThrowIfNegative(maxWidth);

        var minWidth = 0;
        var desiredWidth = 0;

        var y = 0;
        foreach (var child in _children)
        {
            var measurement = MeasureChild(child, in context,
                HPD.TUI.Layout.LayoutConstraints.Loose(maxWidth, context.Height), 0, y);
            minWidth = Math.Max(minWidth, measurement.MinWidth);
            desiredWidth = Math.Max(desiredWidth, measurement.MaxWidth);
            y += measurement.Height + 1;
        }

        return new Measurement(minWidth, Math.Min(maxWidth, desiredWidth));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        for (var i = 0; i < _children.Count; i++)
        {
            output.Render(_children[i], in context, maxWidth);

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

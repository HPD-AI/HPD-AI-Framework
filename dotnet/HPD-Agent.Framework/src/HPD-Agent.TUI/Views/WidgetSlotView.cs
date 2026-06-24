using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Views;

public sealed class WidgetSlotView : IComponent
{
    private readonly WidgetSlotModel _model;
    private readonly Text _empty;
    private readonly List<IComponent> _components = [];
    private int _version = -1;

    public WidgetSlotView(WidgetSlotModel model, string emptyText)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _empty = new Text(emptyText ?? throw new ArgumentNullException(nameof(emptyText)));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        RefreshComponents();
        if (_components.Count == 0)
        {
            return _empty.Measure(in context, maxWidth);
        }

        var min = 0;
        var max = 0;
        var height = 0;
        foreach (var component in _components)
        {
            var measurement = component.Measure(in context, maxWidth);
            min = Math.Max(min, measurement.MinWidth);
            max = Math.Max(max, measurement.MaxWidth);
            height += measurement.Height;
        }

        height += Math.Max(0, _components.Count - 1);
        return new Measurement(Math.Min(min, maxWidth), Math.Min(max, maxWidth), height);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        RefreshComponents();
        if (_components.Count == 0)
        {
            _empty.Render(in context, maxWidth, ref output);
            return;
        }

        for (var i = 0; i < _components.Count; i++)
        {
            _components[i].Render(in context, maxWidth, ref output);
            if (i < _components.Count - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        RefreshComponents();
        foreach (var component in _components)
        {
            if (component.HandleInput(in key))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshComponents()
    {
        var version = _model.Version;
        if (version == _version)
        {
            return;
        }

        _model.CopyTo(_components);
        _version = version;
    }
}

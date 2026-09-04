using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Views;

public sealed class WidgetSlotView : Component
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

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        RefreshComponents();
        if (_components.Count == 0)
        {
            return _empty.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(maxWidth, context.Height));
        }

        var min = 0;
        var max = 0;
        var height = 0;
        foreach (var component in _components)
        {
            var measurement = component.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(maxWidth, context.Height));
            min = Math.Max(min, measurement.MinWidth);
            max = Math.Max(max, measurement.MaxWidth);
            height += measurement.Height;
        }

        height += Math.Max(0, _components.Count - 1);
        return new Measurement(Math.Min(min, maxWidth), Math.Min(max, maxWidth), height);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        RefreshComponents();
        if (_components.Count == 0)
        {
            output.Render(_empty, in context, maxWidth);
            return;
        }

        for (var i = 0; i < _components.Count; i++)
        {
            output.Render(_components[i], in context, maxWidth);
            if (i < _components.Count - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
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

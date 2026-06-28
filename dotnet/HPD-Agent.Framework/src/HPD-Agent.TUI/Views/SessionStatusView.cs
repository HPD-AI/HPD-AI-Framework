using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Views;

public sealed class SessionStatusView : IComponent
{
    private readonly SessionStatusModel _model;
    private readonly List<IComponent> _components = [];
    private int _version = -1;

    public SessionStatusView(SessionStatusModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        RefreshComponents();
        var min = 0;
        var max = 0;
        var height = 0;
        foreach (var component in _components)
        {
            var measurement = component.Measure(in context, maxWidth);
            if (measurement.Height <= 0)
            {
                continue;
            }

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
        var wrote = false;
        foreach (var component in _components)
        {
            if (component.Measure(in context, maxWidth).Height <= 0)
            {
                continue;
            }

            if (wrote)
            {
                output.WriteLineBreak();
            }

            component.Render(in context, maxWidth, ref output);
            wrote = true;
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

        _components.Clear();
        foreach (var entry in _model.Snapshot())
        {
            _components.Add(new Text(entry.Text));
        }

        _version = version;
    }
}

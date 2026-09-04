using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Views;

/// <summary>Renders the registered application-owned footer items.</summary>
public sealed class FooterItemsView : Component
{
    private readonly IComponent[] _components;

    public FooterItemsView(
        ChatShellModel shell,
        AgentTuiStateBag state,
        IReadOnlyList<AgentTuiContribution<IAgentTuiFooterItem>> items)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(items);

        _components = new IComponent[items.Count];
        var footerContext = new AgentTuiFooterContext(shell.Scope, shell, state);
        for (var i = 0; i < items.Count; i++)
        {
            _components[i] = CreateItem(items[i], footerContext);
        }
    }

    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        var min = 0;
        var max = 0;
        var height = 0;
        var visible = 0;
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
            visible++;
        }

        height += Math.Max(0, visible - 1);
        return new Measurement(Math.Min(min, maxWidth), Math.Min(max, maxWidth), height);
    }

    public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        if (_components.Length == 0)
        {
            return;
        }

        var wrote = false;
        for (var i = 0; i < _components.Length; i++)
        {
            if (_components[i].Measure(in context, maxWidth).Height <= 0)
            {
                continue;
            }

            if (wrote)
            {
                output.WriteLineBreak();
            }

            _components[i].Render(in context, maxWidth, ref output);
            wrote = true;
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        foreach (var component in _components)
        {
            if (component.HandleInput(in key))
            {
                return true;
            }
        }

        return false;
    }

    private static IComponent CreateItem(
        AgentTuiContribution<IAgentTuiFooterItem> item,
        AgentTuiFooterContext context)
    {
        try
        {
            return item.Value.Create(context);
        }
        catch (Exception ex)
        {
            return new Text($"{item.Key}: failed ({ex.GetType().Name})");
        }
    }
}

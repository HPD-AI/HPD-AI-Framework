using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Views;

public sealed class ContributionWidgetSlotView : Component
{
    private readonly IComponent[] _components;

    public ContributionWidgetSlotView(
        TuiSlot slot,
        ChatShellModel shell,
        AgentTuiStateBag state,
        IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> widgets)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(widgets);

        _components = new IComponent[widgets.Count];
        var widgetContext = new AgentTuiWidgetContext(slot, shell.Scope, shell, state);
        for (var i = 0; i < widgets.Count; i++)
        {
            _components[i] = CreateWidget(widgets[i], widgetContext);
            AdoptChild(_components[i]);
            if (_components[i] is IFocusable focusable) shell.WidgetFocus.Register(slot, focusable);
        }
    }

    public void RegisterFocus(TuiSlot slot, AgentTuiWidgetFocus focus)
    {
        foreach (var component in _components)
            if (component is IFocusable focusable) focus.Register(slot, focusable);
    }

    // Each editor slot reserves at most one third of the terminal for registered widgets.
    // Allocate shared space fairly; a short widget returns its unused rows to later widgets.
    private int[] Allocate(in RenderContext context, int width, int maxHeight)
    {
        var budget = Math.Min(maxHeight, context.Height / 3);
        var heights = new int[_components.Length];
        var visible = new List<int>();
        for (var i = 0; i < _components.Length; i++)
            if (MeasureChild(_components[i], in context, HPD.TUI.Layout.LayoutConstraints.Loose(width, budget)).Height > 0)
                visible.Add(i);
        budget = Math.Max(0, budget - Math.Max(0, visible.Count - 1));
        for (var n = 0; n < visible.Count; n++)
        {
            var share = budget / (visible.Count - n);
            var i = visible[n];
            heights[i] = Math.Min(share, MeasureChild(_components[i], in context,
                HPD.TUI.Layout.LayoutConstraints.Loose(width, share)).Height);
            budget -= heights[i];
        }
        return heights;
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var heights = Allocate(in context, constraints.MaxWidth, constraints.MaxHeight);
        var visible = heights.Count(height => height > 0);
        return new(0, constraints.MaxWidth, heights.Sum() + Math.Max(0, visible - 1));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var heights = Allocate(in context, output.MaxWidth, context.Height);
        var wrote = false;
        for (var i = 0; i < _components.Length; i++)
        {
            var height = heights[i];
            if (height <= 0) continue;
            if (wrote) output.WriteLineBreak();
            var y = output.CursorY;
            var childContext = new RenderContext(context.Width, height, context.Theme,
                context.ColorSystem, context.Elapsed, context.Capabilities);
            output.PushClip(new HPD.TUI.Layout.LayoutRect(0, y, output.MaxWidth, height));
            output.Render(_components[i], in childContext, output.MaxWidth);
            output.PopClip();
            output.MoveTo(0, y + height - 1);
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

    private static IComponent CreateWidget(
        AgentTuiContribution<IAgentTuiWidget> widget,
        AgentTuiWidgetContext context)
    {
        try
        {
            return widget.Value.Create(context);
        }
        catch (Exception ex)
        {
            return new Text($"{widget.Key}: failed ({ex.GetType().Name})");
        }
    }
}

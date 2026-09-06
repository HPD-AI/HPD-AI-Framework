using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

/// <summary>A registered interactive widget can opt out of traversal while hidden or disabled.</summary>
public interface IAgentTuiFocusableWidget : IFocusable
{
    bool CanFocus { get; }
}

/// <summary>Shell-owned focus order for registered widgets in both editor slots.</summary>
public sealed class AgentTuiWidgetFocus
{
    private readonly List<(TuiSlot Slot, IFocusable Widget)> _widgets = [];

    public void Clear() => _widgets.Clear();

    public void Register(TuiSlot slot, IFocusable widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (!_widgets.Any(item => ReferenceEquals(item.Widget, widget))) _widgets.Add((slot, widget));
    }

    public IFocusable Next(IComponent current, IFocusable prompt, IEnumerable<IFocusable> dynamicWidgets)
    {
        var eligible = _widgets.OrderBy(item => item.Slot).Select(item => item.Widget)
            .Concat(dynamicWidgets).Distinct()
            .Where(widget => widget is not IAgentTuiFocusableWidget conditional || conditional.CanFocus).ToList();
        if (ReferenceEquals(current, prompt)) return eligible.FirstOrDefault() ?? prompt;
        var index = eligible.FindIndex(widget => ReferenceEquals(widget, current));
        return index >= 0 && index + 1 < eligible.Count ? eligible[index + 1] : prompt;
    }
}

using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;

namespace HPD.Agent.TUI.Tests;

public class WidgetFocusTests
{
    [Fact]
    public void TraversalIncludesBothSlotsAndSkipsHiddenWidgets()
    {
        var focus = new AgentTuiWidgetFocus(); var prompt = new Widget(); var a = new Widget(); var b = new Widget();
        var hidden = new Widget { CanFocus = false };
        focus.Register(TuiSlot.BelowEditor, b); focus.Register(TuiSlot.AboveEditor, a); focus.Register(TuiSlot.AboveEditor, hidden);
        Assert.Same(a, focus.Next(prompt, prompt, []));
        Assert.Same(b, focus.Next(a, prompt, []));
        Assert.Same(prompt, focus.Next(b, prompt, []));
        a.CanFocus = false; Assert.Same(b, focus.Next(prompt, prompt, []));
    }

    [Fact]
    public void ContributionOwnsChildRevisionsAndBoundsOverflow()
    {
        var widget = new Widget();
        var shell = new ChatShellModel(new AgentTuiRuntimeScope("a", "s", "t"));
        var view = new ContributionWidgetSlotView(TuiSlot.AboveEditor, shell, new(), [new("test", new Factory(widget))]);
        var revision = view.LayoutRevision;
        widget.Change();
        Assert.NotEqual(revision, view.LayoutRevision);
        var context = new RenderContext(20, 12, Theme.Default);
        Assert.InRange(view.Measure(context, LayoutConstraints.Loose(20, 12)).Height, 0, 4);
        var lines = TuiCapture.RenderToLines(view, width: 20, height: 12, trimTrailingBlankLines: true);
        Assert.True(lines.Length <= 4);
    }

    private sealed class Factory(Widget widget) : IAgentTuiWidget
    { public IComponent Create(AgentTuiWidgetContext context) => widget; }
    private sealed class Widget : Component, IAgentTuiFocusableWidget
    {
        public bool CanFocus { get; set; } = true;
        public bool IsFocused { get; set; }
        public void Change() => InvalidateLayout();
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(0, 20, 50);
        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        { for (var i = 0; i < 50; i++) { output.Write("row", context.Theme.Text); output.WriteLineBreak(); } }
        public override bool HandleInput(in TuiInputEvent input) => false;
    }
}

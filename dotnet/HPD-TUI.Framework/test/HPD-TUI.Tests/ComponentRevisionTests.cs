using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.TUI.Tests;

public sealed class ComponentRevisionTests
{
    [Fact]
    public void TextMutations_AdvanceOnlyRequiredRevisions()
    {
        var text = new Text("old");
        var layout = text.LayoutRevision;
        var paint = text.PaintRevision;

        text.SetStyle(new Style(new Color(255, 0, 0), Color.Default));

        Assert.Equal(layout, text.LayoutRevision);
        Assert.NotEqual(paint, text.PaintRevision);
        paint = text.PaintRevision;

        text.SetText("new");

        Assert.NotEqual(layout, text.LayoutRevision);
        Assert.NotEqual(paint, text.PaintRevision);
    }

    [Fact]
    public void Container_RejectsASecondOwningParent()
    {
        var child = new Text("child");
        var first = new Container();
        var second = new Container();
        first.Add(child);

        Assert.Throws<InvalidOperationException>(() => second.Add(child));
        Assert.True(first.Remove(child));
        second.Add(child);
        Assert.Same(child, Assert.Single(second.Children));
    }

    [Fact]
    public void Surface_RecursivelyAttachesDynamicChildrenAndIgnoresDetachedInvalidation()
    {
        var renders = 0;
        var root = new Container();
        var child = new Text("first");
        root.Add(child);
        var surface = new ComponentSurface(() => renders++);

        surface.ReplaceRoot(root);
        var afterAttach = renders;
        child.SetText("attached");
        Assert.True(renders > afterAttach);

        var added = new Text("second");
        root.Add(added);
        var afterAdd = renders;
        added.SetText("attached too");
        Assert.True(renders > afterAdd);

        Assert.True(root.Remove(added));
        var afterRemove = renders;
        added.SetText("detached");
        Assert.Equal(afterRemove, renders);
    }

    [Fact]
    public void Surface_ReattachmentUsesANewAttachmentGeneration()
    {
        var renders = 0;
        var root = new Text("root");
        var surface = new ComponentSurface(() => renders++);

        surface.ReplaceRoot(root);
        var first = ((IComponent)root).Lifecycle.Attachment!.Value.AttachmentGeneration;
        surface.ReplaceRoot(null);
        surface.ReplaceRoot(root);
        var second = ((IComponent)root).Lifecycle.Attachment!.Value.AttachmentGeneration;

        Assert.NotEqual(first, second);
        root.SetText("live");
        Assert.True(renders > 0);
    }

    [Fact]
    public void AttachedMutation_RequiresOwningMailboxAccess()
    {
        var access = false;
        var text = new Text("root");
        var surface = new ComponentSurface(() => { }, () => access);
        surface.ReplaceRoot(text);

        Assert.Throws<InvalidOperationException>(() => text.SetText("outside"));
        access = true;
        text.SetText("inside");

        Assert.Equal("inside", text.Value);
    }

    [Fact]
    public void Container_MeasurementCacheUsesChildLayoutRevision()
    {
        var child = new MeasuredComponent();
        var container = new Container();
        container.Add(child);
        var context = new RenderContext(80, 24, Theme.Default);

        container.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(80, context.Height));
        container.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(80, context.Height));
        Assert.Equal(1, child.MeasureCount);

        child.ChangeLayout();
        container.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(80, context.Height));
        Assert.Equal(2, child.MeasureCount);
    }

    [Fact]
    public void Container_DoesNotCacheOptOutMeasurements()
    {
        var child = new UncachedMeasuredComponent();
        var container = new Container();
        container.Add(child);
        var context = new RenderContext(80, 24, Theme.Default);

        container.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(80, context.Height));
        container.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(80, context.Height));

        Assert.Equal(2, child.MeasureCount);
    }

    private sealed class MeasuredComponent : Component
    {
        public int MeasureCount { get; private set; }
        public override ComponentDependencies Dependencies => new(RenderContextFields.Width, RenderContextFields.None);
        public void ChangeLayout() => InvalidateLayout();
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        {
            var maxWidth = constraints.MaxWidth;
            MeasureCount++;
            return new(1, 1, 1);
        }
        public override void Render(in RenderContext context, ref DisplayListBuilder output) { }
    }

    private sealed class UncachedMeasuredComponent : Component
    {
        public int MeasureCount { get; private set; }
        public override LayoutCachePolicy LayoutCachePolicy => LayoutCachePolicy.None;
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        {
            MeasureCount++;
            return new(1, 1, 1);
        }
        public override void Render(in RenderContext context, ref DisplayListBuilder output) { }
    }
}

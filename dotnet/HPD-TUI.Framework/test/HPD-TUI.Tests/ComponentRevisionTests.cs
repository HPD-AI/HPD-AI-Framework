using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Models;
using HPD.TUI.Controllers;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class ComponentRevisionTests
{
    [Fact]
    public void OverlayMutations_TrackLayoutAndPaintCategories()
    {
        var overlay = new Overlay(new Text("child"), 0, 0, 10);
        var layout = overlay.LayoutRevision;
        var paint = overlay.PaintRevision;

        overlay.ClearBackground = true;

        Assert.Equal(layout, overlay.LayoutRevision);
        Assert.NotEqual(paint, overlay.PaintRevision);
        paint = overlay.PaintRevision;

        overlay.X = 2;

        Assert.NotEqual(layout, overlay.LayoutRevision);
        Assert.NotEqual(paint, overlay.PaintRevision);
    }

    [Fact]
    public void ViewportMutations_TrackContentLayoutAndScrollingPaint()
    {
        var viewport = new Viewport(2);
        var layout = viewport.LayoutRevision;
        var paint = viewport.PaintRevision;

        viewport.AddLine("first");

        Assert.NotEqual(layout, viewport.LayoutRevision);
        Assert.NotEqual(paint, viewport.PaintRevision);
        viewport.AddLine("second");
        viewport.AddLine("third");
        layout = viewport.LayoutRevision;
        paint = viewport.PaintRevision;

        viewport.ScrollBy(1);

        Assert.Equal(layout, viewport.LayoutRevision);
        Assert.NotEqual(paint, viewport.PaintRevision);
    }

    [Fact]
    public void ViewProperties_TrackFocusPaintAndPresentationLayout()
    {
        var model = new CollectionModel<string>();
        model.Add(new CollectionItem<string>("one", "one", "One"));
        var navigation = new CollectionNavigationController<string>(model);
        var view = new CollectionListView<string>(model, navigation);
        var layout = view.LayoutRevision;
        var paint = view.PaintRevision;

        view.IsFocused = true;

        Assert.Equal(layout, view.LayoutRevision);
        Assert.NotEqual(paint, view.PaintRevision);
        paint = view.PaintRevision;

        view.Mode = CollectionListMode.Checklist;

        Assert.NotEqual(layout, view.LayoutRevision);
        Assert.NotEqual(paint, view.PaintRevision);
    }

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
        var access = true;
        var text = new Text("root");
        var surface = new ComponentSurface(() => { }, () => access);
        surface.ReplaceRoot(text);

        access = false;
        Assert.Throws<InvalidOperationException>(() => text.SetText("outside"));
        Assert.Equal("root", text.Value);
        access = true;
        text.SetText("inside");

        Assert.Equal("inside", text.Value);
    }

    [Fact]
    public void Container_MeasurementCacheUsesChildLayoutRevision()
    {
        var child = new MeasuredComponent();
        var container = new ConstraintCacheRoot(child);
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

    [Fact]
    public void MeasurementCache_UsesAllConstraintsAndDeclaredCapabilities()
    {
        var child = new CapabilityMeasuredComponent();
        var container = new ConstraintCacheRoot(child);
        var first = new RenderContext(80, 24, Theme.Default, capabilities: TerminalCapabilities.None);
        var capable = new RenderContext(80, 24, Theme.Default, capabilities: TerminalCapabilities.Hyperlinks);

        container.MeasureWith(first, new HPD.TUI.Layout.LayoutConstraints(0, 80, 0, 24));
        container.MeasureWith(first, new HPD.TUI.Layout.LayoutConstraints(10, 80, 0, 24));
        container.MeasureWith(capable, new HPD.TUI.Layout.LayoutConstraints(10, 80, 0, 24));

        Assert.Equal(3, child.MeasureCount);
    }

    [Fact]
    public void DescendantLayoutInvalidation_AdvancesEveryAttachedAncestor()
    {
        var leaf = new MeasuredComponent();
        var middle = new Container();
        middle.Add(leaf);
        var root = new Container();
        root.Add(middle);
        var surface = new ComponentSurface(() => { });
        surface.ReplaceRoot(root);
        var rootRevision = root.LayoutRevision;
        var middleRevision = middle.LayoutRevision;

        leaf.ChangeLayout();

        Assert.NotEqual(rootRevision, root.LayoutRevision);
        Assert.NotEqual(middleRevision, middle.LayoutRevision);
    }

    [Fact]
    public void RootReplacement_PrevalidationLeavesCurrentTreeAttached()
    {
        var current = new Text("current");
        var owner = new Container();
        var invalidRoot = new Text("owned");
        owner.Add(invalidRoot);
        var surface = new ComponentSurface(() => { });
        surface.ReplaceRoot(current);

        Assert.Throws<InvalidOperationException>(() => surface.ReplaceRoot(invalidRoot));

        Assert.Same(current, surface.Root);
        Assert.NotNull(((IComponent)current).Lifecycle.Attachment);
        Assert.Null(((IComponent)invalidRoot).Lifecycle.Attachment);
    }

    [Fact]
    public void GridRowAdoption_PrevalidatesEveryCellBeforeChangingOwnership()
    {
        var available = new Text("available");
        var owned = new Text("owned");
        var otherParent = new Container();
        otherParent.Add(owned);
        var grid = new Grid().AddColumn(SizePolicy.Content()).AddColumn(SizePolicy.Content());

        Assert.Throws<InvalidOperationException>(() => grid.AddRow(available, owned));

        Assert.Null(((IComponent)available).Lifecycle.OwnerParent);
        Assert.Empty(grid.Rows);
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

    private sealed class CapabilityMeasuredComponent : Component
    {
        public int MeasureCount { get; private set; }
        public override ComponentDependencies Dependencies =>
            new(RenderContextFields.Capabilities, RenderContextFields.None);
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        {
            MeasureCount++;
            return new(constraints.MinWidth, constraints.MaxWidth, constraints.MinHeight);
        }
        public override void Render(in RenderContext context, ref DisplayListBuilder output) { }
    }

    private sealed class ConstraintCacheRoot : Component
    {
        private readonly IComponent _child;
        public ConstraintCacheRoot(IComponent child) { _child = child; AdoptChild(child); }
        public Measurement MeasureWith(RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) =>
            MeasureChild(_child, in context, constraints);
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) =>
            MeasureChild(_child, in context, constraints);
        public override void Render(in RenderContext context, ref DisplayListBuilder output) =>
            output.Render(_child, in context, output.MaxWidth);
    }
}

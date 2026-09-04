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
}

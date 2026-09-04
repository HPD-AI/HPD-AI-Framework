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
}

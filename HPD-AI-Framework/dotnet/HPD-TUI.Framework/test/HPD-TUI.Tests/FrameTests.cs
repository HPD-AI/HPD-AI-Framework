using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class FrameTests
{
    [Fact]
    public void Render_DrawsHeaderAndFooter()
    {
        var frame = Frame.Create(new Text("body"))
            .WithHeader("Title", Alignment.Center)
            .WithFooter("Done", Alignment.End);

        var lines = TuiCapture.RenderToLines(frame, 10, 4);

        Assert.Equal("┌ Title ─┐", lines[0]);
        Assert.Equal("│body    │", lines[1]);
        Assert.Equal("└── Done ┘", lines[2]);
    }

    [Fact]
    public void Render_AppliesPadding()
    {
        var frame = Frame.Create(new Text("x")).WithPadding(new Thickness(1, 2));

        var lines = TuiCapture.RenderToLines(frame, 7, 5);

        Assert.Equal("┌─────┐", lines[0]);
        Assert.Equal("│     │", lines[1]);
        Assert.Equal("│  x  │", lines[2]);
        Assert.Equal("│     │", lines[3]);
        Assert.Equal("└─────┘", lines[4]);
    }

    [Fact]
    public void Render_UsesBorderSpec()
    {
        var frame = Frame.Create(new Text("x")).WithBorder(BorderSpec.Ascii);

        var lines = TuiCapture.RenderToLines(frame, 4, 3);

        Assert.Equal("+--+", lines[0]);
        Assert.Equal("|x |", lines[1]);
        Assert.Equal("+--+", lines[2]);
    }

    [Fact]
    public void Render_HandlesWidthOne()
    {
        var frame = Frame.Create(new Text("x"));

        var lines = TuiCapture.RenderToLines(frame, 1, 2);

        Assert.Equal("─", lines[0]);
        Assert.Equal("─", lines[1]);
    }
}

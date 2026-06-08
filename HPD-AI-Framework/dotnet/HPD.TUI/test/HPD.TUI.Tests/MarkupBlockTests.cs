using HPD.TUI.Content;
using HPD.TUI.Core;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class MarkupBlockTests
{
    [Fact]
    public void Parse_CreatesStyledRuns()
    {
        var block = new MarkupBlock("[green]Done[/] [bold]now[/]");

        Assert.Equal(ContentBlockKind.Markup, block.Kind);
        Assert.Equal(3, block.Runs.Length);
        Assert.Equal("Done", block.Runs[0].Text);
        Assert.Equal(Theme.Default.Success.Foreground, block.Runs[0].Style.Foreground);
        Assert.Equal(" ", block.Runs[1].Text);
        Assert.Equal("now", block.Runs[2].Text);
        Assert.True(block.Runs[2].Style.Attributes.HasFlag(TextAttributes.Bold));
    }

    [Fact]
    public void Parse_NestedStylesRestoreParentStyle()
    {
        var block = new MarkupBlock("[red]outer [bold]inner[/] outer[/]");

        Assert.Equal("outer ", block.Runs[0].Text);
        Assert.Equal(Theme.Default.Error.Foreground, block.Runs[0].Style.Foreground);
        Assert.False(block.Runs[0].Style.Attributes.HasFlag(TextAttributes.Bold));

        Assert.Equal("inner", block.Runs[1].Text);
        Assert.Equal(Theme.Default.Error.Foreground, block.Runs[1].Style.Foreground);
        Assert.True(block.Runs[1].Style.Attributes.HasFlag(TextAttributes.Bold));

        Assert.Equal(" outer", block.Runs[2].Text);
        Assert.Equal(Theme.Default.Error.Foreground, block.Runs[2].Style.Foreground);
        Assert.False(block.Runs[2].Style.Attributes.HasFlag(TextAttributes.Bold));
    }

    [Fact]
    public void Parse_EscapesBrackets()
    {
        var block = new MarkupBlock("[[green]] literal");

        Assert.Equal(1, block.Runs.Length);
        Assert.Equal("[green] literal", block.Runs[0].Text);
        Assert.Empty(block.ParseDiagnostics);
    }

    [Fact]
    public void Parse_UnknownTagFallsBackToLiteralText()
    {
        var block = new MarkupBlock("[sparkle]text[/]");

        Assert.Equal("[sparkle]text", TuiCapture.RenderToString(block, 20, 1).TrimEnd());
        Assert.NotEmpty(block.ParseDiagnostics);
    }

    [Fact]
    public void Render_RemovesMarkupAndWrapsAcrossRuns()
    {
        var block = new MarkupBlock("[green]hello[/] [bold]world[/]");

        var lines = TuiCapture.RenderToLines(block, 8, 2);

        Assert.Equal("hello wo", lines[0]);
        Assert.Equal("rld     ", lines[1]);
    }
}

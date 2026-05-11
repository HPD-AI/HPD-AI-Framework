using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Markdown;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class MarkdownTests
{
    [Fact]
    public void Render_StripsHeadingMarker()
    {
        var markdown = new HPD.TUI.Components.Markdown("# Title");
        var context = new RenderContext(20, 2, Theme.Default);
        using var grid = new TerminalGrid(20, 2);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 20, ref writer);

        Assert.Equal(new Rune('T'), grid.GetCell(0, 0).Rune);
    }

    [Fact]
    public void Render_UsesBulletGlyphForListItems()
    {
        var markdown = new HPD.TUI.Components.Markdown("- item");
        var context = new RenderContext(20, 2, Theme.Default);
        using var grid = new TerminalGrid(20, 2);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 20, ref writer);

        Assert.Equal(new Rune('•'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune('i'), grid.GetCell(2, 0).Rune);
    }

    [Fact]
    public void Render_InlineCodeUsesAccentForeground()
    {
        var markdown = new HPD.TUI.Components.Markdown("Use `code` now");
        var context = new RenderContext(30, 2, Theme.Default);
        using var grid = new TerminalGrid(30, 2);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 30, ref writer);

        Assert.Equal(new Rune('c'), grid.GetCell(4, 0).Rune);
        Assert.Equal(Color.Cyan, grid.GetCell(4, 0).Style.Foreground);
    }

    [Fact]
    public void Render_FencedCodeUsesBorderAndHighlightsKeywords()
    {
        var markdown = new HPD.TUI.Components.Markdown("""
```csharp
public class Demo
```
""");
        var context = new RenderContext(40, 4, Theme.Default);
        using var grid = new TerminalGrid(40, 4);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 40, ref writer);

        Assert.Equal(new Rune('╭'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune('│'), grid.GetCell(0, 1).Rune);
        Assert.Equal(Color.Cyan, grid.GetCell(2, 1).Style.Foreground);
    }

    [Fact]
    public void Render_QuoteUsesSuccessPrefix()
    {
        var markdown = new HPD.TUI.Components.Markdown("> quoted");
        var context = new RenderContext(30, 2, Theme.Default);
        using var grid = new TerminalGrid(30, 2);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 30, ref writer);

        Assert.Equal(new Rune('|'), grid.GetCell(0, 0).Rune);
        Assert.Equal(Color.Green, grid.GetCell(0, 0).Style.Foreground);
    }

    [Fact]
    public void Parser_DoesNotSplitInsideOpenCodeFence()
    {
        var markdown = "before\n\n```csharp\npublic";

        Assert.Equal(8, MarkdownParser.FindLastSafeSplitPoint(markdown));
        Assert.True(MarkdownParser.IsInsideCodeBlock(markdown, markdown.Length));
    }

    [Fact]
    public void StreamCollector_CommitsSafeChunksAsTuiComponents()
    {
        var collector = new StreamCollector<IComponent>(new TuiMarkdownRenderer());

        collector.Push("hello\n\n");
        collector.CommitCompleteLines();

        var queued = collector.GetQueuedLines();
        Assert.Single(queued);
        Assert.IsType<HPD.TUI.Components.Markdown>(queued[0]);
    }

    [Fact]
    public void Render_AutolinkUsesUnderlinedAccent()
    {
        var markdown = new HPD.TUI.Components.Markdown("https://example.com");
        var context = new RenderContext(40, 2, Theme.Default);
        using var grid = new TerminalGrid(40, 2);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 40, ref writer);

        Assert.Equal(new Rune('h'), grid.GetCell(0, 0).Rune);
        Assert.Equal(Color.Cyan, grid.GetCell(0, 0).Style.Foreground);
        Assert.True(grid.GetCell(0, 0).Style.Attributes.HasFlag(TextAttributes.Underline));
    }

    [Fact]
    public void Render_StrikethroughUsesAnsiAttribute()
    {
        var markdown = new HPD.TUI.Components.Markdown("~~gone~~");
        var context = new RenderContext(20, 2, Theme.Default);
        using var grid = new TerminalGrid(20, 2);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 20, ref writer);

        Assert.Equal(new Rune('g'), grid.GetCell(0, 0).Rune);
        Assert.True(grid.GetCell(0, 0).Style.Attributes.HasFlag(TextAttributes.Strikethrough));
    }

    [Fact]
    public void Render_NestedListUsesNestedBullet()
    {
        var markdown = new HPD.TUI.Components.Markdown("""
- parent
  - child
""");
        var context = new RenderContext(30, 4, Theme.Default);
        using var grid = new TerminalGrid(30, 4);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 30, ref writer);

        Assert.Equal(new Rune('•'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune('o'), grid.GetCell(2, 1).Rune);
    }

    [Fact]
    public void Render_TableHeaderUsesBold()
    {
        var markdown = new HPD.TUI.Components.Markdown("""
| A | B |
|---|---|
| 1 | 2 |
""");
        var context = new RenderContext(40, 4, Theme.Default);
        using var grid = new TerminalGrid(40, 4);
        var writer = new SegmentWriter(grid);

        markdown.Render(in context, 40, ref writer);

        Assert.Equal(new Rune('A'), grid.GetCell(2, 0).Rune);
        Assert.True(grid.GetCell(2, 0).Style.Attributes.HasFlag(TextAttributes.Bold));
    }
}

using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Markdown;
using HPD.TUI.Rendering;
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
    public void Render_WrappedListItemUsesHangingIndent()
    {
        var markdown = new HPD.TUI.Components.Markdown("- alpha beta gamma");

        var lines = TuiCapture.RenderToLines(markdown, 12, 4, trimTrailingBlankLines: true);

        Assert.Equal("• alpha beta", lines[0].TrimEnd());
        Assert.Equal("  gamma", lines[1].TrimEnd());
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
        Assert.Equal(Theme.Default.Accent.Foreground, grid.GetCell(4, 0).Style.Foreground);
        Assert.Equal(Theme.Default.Text.Background, grid.GetCell(4, 0).Style.Background);
    }

    [Fact]
    public void Render_FencedCodeUsesPlainHeaderAndHighlightsKeywords()
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

        Assert.Equal(new Rune('c'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune(' '), grid.GetCell(0, 1).Rune);
        Assert.Equal(Theme.Default.Accent.Foreground, grid.GetCell(2, 1).Style.Foreground);
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
        Assert.Equal(Theme.Default.Success.Foreground, grid.GetCell(0, 0).Style.Foreground);
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
        Assert.Equal(Theme.Default.Accent.Foreground, grid.GetCell(0, 0).Style.Foreground);
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

        Assert.Equal(new Rune('A'), grid.GetCell(2, 1).Rune);
        Assert.True(grid.GetCell(2, 1).Style.Attributes.HasFlag(TextAttributes.Bold));
    }

    [Fact]
    public void Render_TableUsesBoxLayout()
    {
        var markdown = new HPD.TUI.Components.Markdown("""
| Name | Kind |
|---|---|
| alpha | file |
""");

        var lines = TuiCapture.RenderToLines(markdown, 32, 6, trimTrailingBlankLines: true);

        Assert.Equal("┌───────┬──────┐", lines[0].TrimEnd());
        Assert.Equal("│ Name  │ Kind │", lines[1].TrimEnd());
        Assert.Equal("├───────┼──────┤", lines[2].TrimEnd());
        Assert.Equal("│ alpha │ file │", lines[3].TrimEnd());
        Assert.Equal("└───────┴──────┘", lines[4].TrimEnd());
    }

    [Fact]
    public void Render_TableWrapsLongCells()
    {
        var markdown = new HPD.TUI.Components.Markdown("""
| Item | Notes |
|---|---|
| alpha | needs careful wrapping |
""");

        var lines = TuiCapture.RenderToLines(markdown, 24, 8, trimTrailingBlankLines: true);

        Assert.Contains("needs", string.Join('\n', lines));
        Assert.Contains("careful", string.Join('\n', lines));
        Assert.Contains("wrapping", string.Join('\n', lines));
        Assert.All(lines, line => Assert.True(line.Length <= 24));
    }

    [Fact]
    public void Render_TableFallsBackToRawMarkdownWhenTooNarrow()
    {
        var markdown = new HPD.TUI.Components.Markdown("""
| A | B | C |
|---|---|---|
| 1 | 2 | 3 |
""");

        var lines = TuiCapture.RenderToLines(markdown, 8, 6, trimTrailingBlankLines: true);
        var rendered = string.Join('\n', lines);

        Assert.Contains("| A |", rendered);
        Assert.Contains("---", rendered);
        Assert.DoesNotContain('┌', rendered);
    }
}

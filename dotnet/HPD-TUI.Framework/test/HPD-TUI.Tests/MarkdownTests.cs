using HPD.TUI.Components;
using HPD.TUI.Content;
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
        var markdown = MarkdownBlock.Prepare("# Title", 20, Theme.Default);
        var context = new RenderContext(20, 2, Theme.Default);
        using var grid = new TerminalGrid(20, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('T'), grid.GetLeadingRune(grid.GetCell(0, 0)));
    }

    [Fact]
    public void Render_UsesBulletGlyphForListItems()
    {
        var markdown = MarkdownBlock.Prepare("- item", 20, Theme.Default);
        var context = new RenderContext(20, 2, Theme.Default);
        using var grid = new TerminalGrid(20, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('•'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(new Rune('i'), grid.GetLeadingRune(grid.GetCell(2, 0)));
    }

    [Fact]
    public void Render_WrappedListItemUsesHangingIndent()
    {
        var markdown = MarkdownBlock.Prepare("- alpha beta gamma", 12, Theme.Default);

        var lines = TuiCapture.RenderToLines(markdown, 12, 4, trimTrailingBlankLines: true);

        Assert.Equal("• alpha beta", lines[0].TrimEnd());
        Assert.Equal("  gamma", lines[1].TrimEnd());
    }

    [Fact]
    public void Render_InlineCodeUsesAccentForeground()
    {
        var markdown = MarkdownBlock.Prepare("Use `code` now", 30, Theme.Default);
        var context = new RenderContext(30, 2, Theme.Default);
        using var grid = new TerminalGrid(30, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('c'), grid.GetLeadingRune(grid.GetCell(4, 0)));
        Assert.Equal(Theme.Default.Accent.Foreground, grid.GetCell(4, 0).Style.Foreground);
        Assert.Equal(Theme.Default.Text.Background, grid.GetCell(4, 0).Style.Background);
    }

    [Fact]
    public void Render_FencedCodeUsesPlainHeaderAndHighlightsKeywords()
    {
        var markdown = MarkdownBlock.Prepare("""
```csharp
public class Demo
```
""", 40, Theme.Default);
        var context = new RenderContext(40, 4, Theme.Default);
        using var grid = new TerminalGrid(40, 4);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('c'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(new Rune(' '), grid.GetLeadingRune(grid.GetCell(0, 1)));
        Assert.Equal(Theme.Default.Accent.Foreground, grid.GetCell(2, 1).Style.Foreground);
    }

    [Fact]
    public void Render_QuoteUsesSuccessPrefix()
    {
        var markdown = MarkdownBlock.Prepare("> quoted", 30, Theme.Default);
        var context = new RenderContext(30, 2, Theme.Default);
        using var grid = new TerminalGrid(30, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('|'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(Theme.Default.Success.Foreground, grid.GetCell(0, 0).Style.Foreground);
    }

    [Fact]
    public void Render_AutolinkUsesUnderlinedAccent()
    {
        var markdown = MarkdownBlock.Prepare("https://example.com", 40, Theme.Default);
        var context = new RenderContext(40, 2, Theme.Default);
        using var grid = new TerminalGrid(40, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('h'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(Theme.Default.Accent.Foreground, grid.GetCell(0, 0).Style.Foreground);
        Assert.True(grid.GetCell(0, 0).Style.Attributes.HasFlag(TextAttributes.Underline));
    }

    [Fact]
    public void Render_StrikethroughUsesAnsiAttribute()
    {
        var markdown = MarkdownBlock.Prepare("~~gone~~", 20, Theme.Default);
        var context = new RenderContext(20, 2, Theme.Default);
        using var grid = new TerminalGrid(20, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('g'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.True(grid.GetCell(0, 0).Style.Attributes.HasFlag(TextAttributes.Strikethrough));
    }

    [Fact]
    public void Render_NestedListUsesNestedBullet()
    {
        var markdown = MarkdownBlock.Prepare("""
- parent
  - child
""", 30, Theme.Default);
        var context = new RenderContext(30, 4, Theme.Default);
        using var grid = new TerminalGrid(30, 4);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('•'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(new Rune('o'), grid.GetLeadingRune(grid.GetCell(2, 1)));
    }

    [Fact]
    public void Render_TableHeaderUsesBold()
    {
        var markdown = MarkdownBlock.Prepare("""
| A | B |
|---|---|
| 1 | 2 |
""", 40, Theme.Default);
        var context = new RenderContext(40, 4, Theme.Default);
        using var grid = new TerminalGrid(40, 4);
        var writer = new DisplayListBuilder(grid, grid.Width);

        markdown.Render(in context, ref writer);

        Assert.Equal(new Rune('A'), grid.GetLeadingRune(grid.GetCell(2, 1)));
        Assert.True(grid.GetCell(2, 1).Style.Attributes.HasFlag(TextAttributes.Bold));
    }

    [Fact]
    public void Render_TableUsesBoxLayout()
    {
        var markdown = MarkdownBlock.Prepare("""
| Name | Kind |
|---|---|
| alpha | file |
""", 32, Theme.Default);

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
        var markdown = MarkdownBlock.Prepare("""
| Item | Notes |
|---|---|
| alpha | needs careful wrapping |
""", 24, Theme.Default);

        var lines = TuiCapture.RenderToLines(markdown, 24, 8, trimTrailingBlankLines: true);

        Assert.Contains("needs", string.Join('\n', lines));
        Assert.Contains("careful", string.Join('\n', lines));
        Assert.Contains("wrapping", string.Join('\n', lines));
        Assert.All(lines, line => Assert.True(line.Length <= 24));
    }

    [Fact]
    public void Render_TableFallsBackToRawMarkdownWhenTooNarrow()
    {
        var markdown = MarkdownBlock.Prepare("""
| A | B | C |
|---|---|---|
| 1 | 2 | 3 |
""", 8, Theme.Default);

        var lines = TuiCapture.RenderToLines(markdown, 8, 6, trimTrailingBlankLines: true);
        var rendered = string.Join('\n', lines);

        Assert.Contains("| A |", rendered);
        Assert.Contains("---", rendered);
        Assert.DoesNotContain('┌', rendered);
    }
}

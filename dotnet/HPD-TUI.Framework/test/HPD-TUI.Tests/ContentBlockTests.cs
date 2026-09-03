using HPD.TUI.Content;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class ContentBlockTests
{
    [Fact]
    public void TextBlock_RendersTextAndKeepsSemanticKind()
    {
        var block = TextBlock.Create("hello");
        using var grid = Render(block, 8, 1);

        Assert.Equal(ContentBlockKind.Text, block.Kind);
        Assert.Equal("hello   ", ReadLine(grid, 0));
    }

    [Fact]
    public void MarkdownBlock_CachesParsedDocumentAndRenders()
    {
        var block = MarkdownBlock.Prepare("# Title", 10, Theme.Default);
        using var grid = Render(block, 10, 1);

        Assert.Equal(ContentBlockKind.Markdown, block.Kind);
        Assert.NotNull(block.Document);
        Assert.Equal("Title     ", ReadLine(grid, 0));
    }

    [Fact]
    public void CodeBlock_StoresLanguageAndLines()
    {
        var block = CodeBlock.Create("one\ntwo", "txt");
        using var grid = Render(block, 12, 3);

        Assert.Equal(ContentBlockKind.Code, block.Kind);
        Assert.Equal("txt", block.Language);
        Assert.Equal(2, block.Lines.Count);
        Assert.Equal("╭ code txt ╮", ReadLine(grid, 0));
        Assert.Equal("│ one       ", ReadLine(grid, 1));
    }

    [Fact]
    public void KeyValueBlock_RendersEntries()
    {
        var block = new KeyValueBlock()
            .Add("Name", "alpha")
            .Add("Kind", "file");
        using var grid = Render(block, 12, 2);

        Assert.Equal(ContentBlockKind.KeyValue, block.Kind);
        Assert.Equal("Name: alpha ", ReadLine(grid, 0));
        Assert.Equal("Kind: file  ", ReadLine(grid, 1));
    }

    [Fact]
    public void ListBlock_RendersOrderedItems()
    {
        var block = ListBlock.Create(["one", "two"], ordered: true);
        using var grid = Render(block, 8, 2);

        Assert.Equal(ContentBlockKind.List, block.Kind);
        Assert.Equal("1. one  ", ReadLine(grid, 0));
        Assert.Equal("2. two  ", ReadLine(grid, 1));
    }

    [Fact]
    public void SeparatorBlock_RendersTitle()
    {
        var block = SeparatorBlock.Create("Log");
        using var grid = Render(block, 9, 1);

        Assert.Equal(ContentBlockKind.Separator, block.Kind);
        Assert.Equal("── Log ──", ReadLine(grid, 0));
    }

    private static TerminalGrid Render(IContentBlock block, int width, int height)
    {
        var context = new RenderContext(width, height, Theme.Default);
        var grid = new TerminalGrid(width, height);
        var writer = new SegmentWriter(grid);
        block.Render(in context, width, ref writer);
        return grid;
    }

    private static string ReadLine(TerminalGrid grid, int y)
    {
        Span<char> buffer = stackalloc char[grid.Width];
        for (var x = 0; x < grid.Width; x++)
        {
            buffer[x] = (char)grid.GetLeadingRune(grid.GetCell(x, y)).Value;
        }

        return new string(buffer);
    }
}

using HPD.TUI.Core;
using HPD.TUI.Markdown;
using HPD.TUI.Components;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class MarkdownPaletteTests
{
    private static readonly Style Red = new(new Color(231, 41, 57), Color.Default);
    private static readonly Style Green = new(new Color(43, 221, 97), Color.Default);

    [Theory]
    [InlineData("cs", "class widget { int value = Compute(42); Matrix item = new Matrix(); }")]
    [InlineData("ts", "class widget { value: number = Compute(42); }")]
    [InlineData("py", "class widget:\n    def Compute(value): return int(value) + 42")]
    public void SyntaxRolesPreserveSourceAndDistinguishCallsTypesAndOperators(string language, string source)
    {
        var syntax = new CodeSyntaxTheme
        {
            Function = Red, Type = Green,
            Operator = new Style(Color.Blue, Color.Default)
        };
        var theme = new MarkdownTheme { Syntax = syntax };
        var result = new BasicCodeHighlighter().Highlight(source.AsMemory(), language, theme);
        Assert.Equal(source, string.Join("\n", result.Lines.Select(line => string.Concat(line.Runs.Select(run => run.Text)))));
        var runs = result.Lines.SelectMany(line => line.Runs).ToArray();
        Assert.Contains(runs, run => run.Text == "widget" && run.Style == syntax.Type);
        Assert.Contains(runs, run => run.Text == "Compute" && run.Style == syntax.Function);
        Assert.Contains(runs, run => (run.Text == "=" || run.Text == "+") && run.Style == syntax.Operator);
        Assert.NotEqual(theme.ThemeKey, (theme with { Syntax = syntax with { Function = Green } }).ThemeKey);
        Assert.NotEqual(theme.ThemeKey, (theme with { Syntax = syntax with { Type = Red } }).ThemeKey);
        Assert.NotEqual(theme.ThemeKey, (theme with { Syntax = syntax with { Operator = Red } }).ThemeKey);
    }

    [Fact]
    public void ElementOverridesAndNestedEmphasisKeepIndependentColors()
    {
        var theme = MarkdownTheme.FromTheme(Theme.Default) with
        {
            Heading1 = Red,
            Heading6 = Green,
            Link = Green,
            InlineCode = Red,
            QuoteText = Green,
            ListMarker = Red,
            TableHeader = Green,
            ThematicBreak = Red
        };
        var layout = Layout("# **title**\n\n###### small\n\n[link](https://example.com) and `code`\n\n> quote\n\n- item\n\n---\n\n| header |\n|---|\n| cell |", theme);
        var runs = layout.Rows.SelectMany(row => row.Line.Runs).ToArray();
        Assert.Contains(runs, run => run.Text == "title" && run.Style.Foreground == Red.Foreground && run.Style.Attributes.HasFlag(TextAttributes.Bold));
        foreach (var word in new[] { "small", "link", "quote", "header" })
            Assert.Contains(runs, run => run.Text.Contains(word) && run.Style.Foreground == Green.Foreground);
        Assert.Contains(runs, run => run.Text == "code" && run.Style == Red);
        Assert.Contains(runs, run => run.Text.Contains('─') && run.Style == Red);
        var nestedLinks = Layout("**[bold link](https://example.com) and `bold code`**", theme);
        Assert.Contains(nestedLinks.Rows.SelectMany(row => row.Line.Runs), run => run.Text == "bold link" &&
            run.Style.Foreground == Green.Foreground && run.Style.Attributes.HasFlag(TextAttributes.Bold));
        Assert.Contains(nestedLinks.Rows.SelectMany(row => row.Line.Runs), run => run.Text == "bold code" &&
            run.Style.Foreground == Red.Foreground && run.Style.Attributes.HasFlag(TextAttributes.Bold));
        var explicitBold = Layout("# **title**", theme with { Strong = new() { Foreground = Green.Foreground, Attributes = TextAttributes.Bold } });
        Assert.Contains(explicitBold.Rows.SelectMany(row => row.Line.Runs), run => run.Text == "title" && run.Style.Foreground == Green.Foreground);
        // A prepared palette is independent of the surrounding shell's UI theme.
        _ = TuiCapture.RenderToString(new MarkdownView(layout), 80, Math.Max(1, layout.Height), Theme.Default with { Accent = Green });
    }

    [Fact]
    public void SyntaxRecognizesCommentsAndDoesNotBorrowHeadingColors()
    {
        var theme = MarkdownTheme.FromTheme(Theme.Default) with
        {
            Heading1 = Red,
            Syntax = new() { Keyword = Green, Comment = Red, String = Green, Number = Red }
        };
        var result = new BasicCodeHighlighter().Highlight("var value = 42; // note\n/* first\nlast */ var text = \"// not a comment\";".AsMemory(), "cs", theme);
        var runs = result.Lines.SelectMany(line => line.Runs).ToArray();
        Assert.Contains(runs, run => run.Text == "var" && run.Style == Green);
        Assert.Contains(runs, run => run.Text == "42" && run.Style == Red);
        Assert.Contains(runs, run => run.Text == "// note" && run.Style == Red);
        Assert.Contains(runs, run => run.Text == "last */" && run.Style == Red);
        Assert.Contains(runs, run => run.Text == "\"// not a comment\"" && run.Style == Green);
        var python = new BasicCodeHighlighter().Highlight("x = '# string' # comment".AsMemory(), "py", theme);
        Assert.Contains(python.Lines[0].Runs, run => run.Text == "# comment" && run.Style == Red);
    }

    [Fact]
    public void RawFallbackUsesConfiguredBodyAndRetainsPaletteIdentity()
    {
        var theme = MarkdownTheme.FromTheme(Theme.Default) with { Body = Red };
        var raw = new MarkdownLayoutEngine().LayoutRaw("raw text", "test", new(80, theme));
        Assert.Equal(theme.ThemeKey, raw.Key.ThemeKey);
        Assert.All(raw.Rows.SelectMany(row => row.Line.Runs), run => Assert.Equal(Red, run.Style));
    }

    private static MarkdownLayout Layout(string source, MarkdownTheme theme)
        => new MarkdownLayoutEngine().Layout(new MarkdownDocumentParser().Parse(source,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() }), new(80, theme));
}

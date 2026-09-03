using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Markdown;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class MarkdownArchitectureTests
{
    [Fact]
    public void Parser_PreservesExactSourceAndNormalizesExclusiveBlockSpans()
    {
        const string source = "# heading\n\nparagraph";
        var parser = new MarkdownDocumentParser();
        var snapshot = parser.Parse(source, new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });

        Assert.Same(source, snapshot.Source);
        Assert.Equal(2, snapshot.Blocks.Count);
        Assert.All(snapshot.Blocks, block => Assert.InRange(block.SourceEndExclusive, block.SourceStart, source.Length));
        Assert.Equal("# heading", source[snapshot.Blocks[0].SourceStart..snapshot.Blocks[0].SourceEndExclusive]);
    }

    [Fact]
    public void MarkdownView_RejectsMismatchedPreparedContext()
    {
        var parser = new MarkdownDocumentParser();
        var snapshot = parser.Parse("hello", new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var layout = new MarkdownLayoutEngine().Layout(snapshot, new(20, MarkdownTheme.FromTheme(Theme.Default)));
        var view = new MarkdownView(layout);
        var context = new RenderContext(19, 2, Theme.Default);
        Assert.Throws<InvalidOperationException>(() => Render(view, context));
    }

    [Fact]
    public void MarkdownView_UnchangedMeasureIsAllocationFree()
    {
        var snapshot = new MarkdownDocumentParser().Parse("prepared", new MarkdownParseOptions
        {
            Pipeline = MarkdownPipelineFactory.CreateDefault()
        });
        var layout = new MarkdownLayoutEngine().Layout(snapshot,
            new(24, MarkdownTheme.FromTheme(Theme.Default)));
        var view = new MarkdownView(layout);
        var context = new RenderContext(24, 4, Theme.Default);
        _ = view.Measure(in context, 24);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++) _ = view.Measure(in context, 24);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void StructuralLink_EmitsBalancedOsc8WithoutAcceptingEscapes()
    {
        Assert.True(TerminalHyperlinkPolicy.TryCreate("https://example.com/path", out var link));
        Assert.False(TerminalHyperlinkPolicy.TryCreate("https://example.com/\u001b]8;;evil", out _));
        using var grid = new TerminalGrid(30, 1);
        var writer = new SegmentWriter(grid);
        writer.Write("example", Theme.Default.Accent, new TerminalRunMetadata(link));

        var ansi = TuiCapture.ToAnsi(grid);
        Assert.Contains("\u001b]8;;https://example.com/path\u001b\\", ansi);
        Assert.Contains("\u001b]8;;\u001b\\", ansi);
    }

    [Theory]
    [InlineData("e\u0301")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("✈️")]
    public void Grid_PreservesCompleteGraphemeClusters(string grapheme)
    {
        using var grid = new TerminalGrid(20, 1);
        grid.Write(grapheme, Style.Default);
        var cell = grid.GetCell(0, 0);

        Assert.Equal(grapheme, grid.GetGrapheme(cell).ToString());
        Assert.False(cell.IsContinuation);
    }

    [Fact]
    public void PipelineIdentity_DeepCopiesOptionsAndDefaultsUnknownExtensionsToGlobal()
    {
        var options = new Dictionary<string, string> { ["mode"] = "one" };
        var implementation = new CustomTerminalExtension();
        var descriptor = MarkdownPipelineFactory.Create(new MarkdownPipelineConfiguration(
            Extensions: [new MarkdownExtensionConfiguration("custom", options)]), [implementation]);
        options["mode"] = "two";
        var extension = Assert.Single(descriptor.Configuration.Extensions!);

        Assert.Equal("one", extension.NormalizedOptions["mode"]);
        Assert.Equal(MarkdownExtensionInvalidation.DocumentGlobal, extension.Invalidation);
        Assert.Equal(1, implementation.ParserConfigurations);
        var snapshot = new MarkdownDocumentParser().Parse("text", new MarkdownParseOptions { Pipeline = descriptor });
        _ = new MarkdownLayoutEngine().Layout(snapshot, new(20, MarkdownTheme.FromTheme(Theme.Default)));
        Assert.True(implementation.TerminalConfigurations > 0);
        Assert.NotEqual(descriptor.StableId, MarkdownPipelineFactory.Create(new MarkdownPipelineConfiguration(
            Extensions: [new MarkdownExtensionConfiguration("custom", new Dictionary<string, string> { ["mode"] = "two" })]), [new CustomTerminalExtension()]).StableId);
    }

    private sealed class CustomTerminalExtension : ITerminalMarkdownExtension
    {
        public int ParserConfigurations { get; private set; }
        public int TerminalConfigurations { get; private set; }
        public string Id => "custom";
        public MarkdownExtensionInvalidation Invalidation => MarkdownExtensionInvalidation.DocumentGlobal;
        public string RendererPolicyId => "test-noop-v1";
        public void ConfigureParser(Markdig.MarkdownPipelineBuilder builder, IReadOnlyDictionary<string, string> options) => ParserConfigurations++;
        public void ConfigureTerminal(Markdig.Renderers.ObjectRendererCollection renderers, IReadOnlyDictionary<string, string> options) => TerminalConfigurations++;
    }

    [Fact]
    public void PipelineConfiguration_ControlsEnabledParserExtensions()
    {
        var parser = new MarkdownDocumentParser();
        var plain = parser.Parse("~~gone~~", new MarkdownParseOptions
        {
            Pipeline = MarkdownPipelineFactory.Create(new MarkdownPipelineConfiguration(Extensions: []))
        });
        var extended = parser.Parse("~~gone~~", new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });

        Assert.DoesNotContain(plain.NodeCapabilities, static name => name.Contains("EmphasisInline", StringComparison.Ordinal));
        Assert.Contains(extended.NodeCapabilities, static name => name.Contains("EmphasisInline", StringComparison.Ordinal));
    }

    [Fact]
    public void Layout_UsesPairwiseSpacingAndStructuralSpacingIdentity()
    {
        var document = new MarkdownDocumentParser().Parse("# heading\n\nparagraph\n\n<div>raw</div>\n",
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var spacing = new MarkdownSpacing { HeadingBottomGap = 2, ParagraphGap = 3 };
        var layout = new MarkdownLayoutEngine().Layout(document,
            new(40, MarkdownTheme.FromTheme(Theme.Default), Spacing: spacing));

        Assert.Equal(spacing.Key, layout.Key.SpacingKey);
        Assert.Equal(2, layout.Rows.Count(static row => row.Kind == MarkdownLayoutRowKind.Separator));
    }

    [Theory]
    [InlineData("plain\u001b[31mred")]
    [InlineData("osc\u0007payload")]
    [InlineData("left\u202eright")]
    [InlineData("nul\0payload")]
    public void RichAndRawLayouts_NeutralizeUnsafeTerminalContent(string source)
    {
        var parser = new MarkdownDocumentParser();
        var snapshot = parser.Parse(source, new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var engine = new MarkdownLayoutEngine();
        foreach (var mode in new[] { MarkdownPresentationMode.Rich, MarkdownPresentationMode.Raw })
        {
            var layout = engine.Layout(snapshot, new(80, MarkdownTheme.FromTheme(Theme.Default), Mode: mode));
            var rendered = string.Concat(layout.Rows.SelectMany(static row => row.Line.Runs).Select(static run => run.Text));
            Assert.DoesNotContain('\u001b', rendered);
            Assert.DoesNotContain('\u0007', rendered);
            Assert.DoesNotContain('\u202e', rendered);
            Assert.DoesNotContain('\0', rendered);
        }
        Assert.Same(source, snapshot.Source);
    }

    [Fact]
    public void Layout_ReflowsByWidthWithoutReparsingOrChangingSource()
    {
        const string source = "one two three four five six seven eight";
        var parser = new MarkdownDocumentParser();
        var snapshot = parser.Parse(source, new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var engine = new MarkdownLayoutEngine();
        var wide = engine.Layout(snapshot, new(40, MarkdownTheme.FromTheme(Theme.Default)));
        var narrow = engine.Layout(snapshot, new(8, MarkdownTheme.FromTheme(Theme.Default)));

        Assert.True(narrow.Height > wide.Height);
        Assert.Same(source, snapshot.Source);
        Assert.Equal(8, narrow.Key.Width);
    }

    private static void Render(IComponent component, RenderContext context)
    {
        using var grid = new TerminalGrid(context.Width, context.Height);
        var writer = new SegmentWriter(grid);
        component.Render(in context, context.Width, ref writer);
    }
}

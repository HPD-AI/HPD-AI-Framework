using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Markdown;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using System.Collections.Immutable;

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
    public void MarkdownView_WarmedRenderIsAllocationFree()
    {
        var snapshot = new MarkdownDocumentParser().Parse("prepared", new MarkdownParseOptions
        {
            Pipeline = MarkdownPipelineFactory.CreateDefault()
        });
        var layout = new MarkdownLayoutEngine().Layout(snapshot,
            new(24, MarkdownTheme.FromTheme(Theme.Default)));
        var view = new MarkdownView(layout);
        var context = new RenderContext(24, 4, Theme.Default);
        using var grid = new TerminalGrid(24, 4);
        var warmWriter = new SegmentWriter(grid);
        view.Render(in context, 24, ref warmWriter);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++)
        {
            grid.Clear();
            var writer = new SegmentWriter(grid);
            view.Render(in context, 24, ref writer);
        }

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
        var paragraph = Assert.Single(snapshot.NodeCapabilities,
            static capability => capability.RuntimeType.EndsWith("ParagraphBlock", StringComparison.Ordinal));
        Assert.Equal(MarkdownTerminalNodeHandling.TypedRenderer, paragraph.TerminalHandling);
        Assert.Contains(nameof(CustomParagraphRenderer), paragraph.RendererType, StringComparison.Ordinal);
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
        public void ConfigureTerminal(Markdig.Renderers.ObjectRendererCollection renderers, IReadOnlyDictionary<string, string> options)
        {
            TerminalConfigurations++;
            renderers.Add(new CustomParagraphRenderer());
        }
    }

    private sealed class CustomParagraphRenderer : TerminalObjectRenderer<Markdig.Syntax.ParagraphBlock>
    {
        protected override void Write(TerminalMarkdownRenderer renderer, Markdig.Syntax.ParagraphBlock node) =>
            renderer.WriteChildren(node.Inline!);
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

        Assert.DoesNotContain(plain.NodeCapabilities, static capability => capability.RuntimeType.Contains("EmphasisInline", StringComparison.Ordinal));
        Assert.Contains(extended.NodeCapabilities, static capability => capability.RuntimeType.Contains("EmphasisInline", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownExtensionLeafAndContainerResolveToSanitizedRegistryFallback()
    {
        var extension = new UnknownNodeExtension();
        var pipeline = MarkdownPipelineFactory.Create(new MarkdownPipelineConfiguration(
            Extensions: [new MarkdownExtensionConfiguration(extension.Id,
                new Dictionary<string, string>(), MarkdownExtensionInvalidation.BlockLocal)]), [extension]);
        var snapshot = new MarkdownDocumentParser().Parse("safe",
            new MarkdownParseOptions { Pipeline = pipeline });

        Assert.Contains(snapshot.NodeCapabilities, capability =>
            capability.RuntimeType.EndsWith(nameof(UnknownLeaf), StringComparison.Ordinal) &&
            capability.TerminalHandling == MarkdownTerminalNodeHandling.SanitizedSourceFallback &&
            capability.RendererType!.EndsWith("LiteralFallbackRenderer", StringComparison.Ordinal));
        Assert.Contains(snapshot.NodeCapabilities, capability =>
            capability.RuntimeType.EndsWith(nameof(UnknownContainer), StringComparison.Ordinal) &&
            capability.TerminalHandling == MarkdownTerminalNodeHandling.SanitizedSourceFallback);
        var layout = new MarkdownLayoutEngine().Layout(snapshot,
            new(20, MarkdownTheme.FromTheme(Theme.Default)));
        Assert.Contains(layout.Rows, row => RowText(row).Contains("safe", StringComparison.Ordinal));
    }

    private sealed class UnknownNodeExtension : ITerminalMarkdownExtension
    {
        public string Id => "unknown-node-test";
        public MarkdownExtensionInvalidation Invalidation => MarkdownExtensionInvalidation.BlockLocal;
        public string RendererPolicyId => "unknown-node-v1";
        public void ConfigureParser(Markdig.MarkdownPipelineBuilder builder, IReadOnlyDictionary<string, string> options) =>
            builder.DocumentProcessed += document =>
            {
                var container = new UnknownContainer { Span = new Markdig.Syntax.SourceSpan(0, 3) };
                container.Add(new UnknownLeaf { Span = new Markdig.Syntax.SourceSpan(0, 3) });
                document.Add(container);
            };
        public void ConfigureTerminal(Markdig.Renderers.ObjectRendererCollection renderers, IReadOnlyDictionary<string, string> options) { }
    }

    private sealed class UnknownLeaf() : Markdig.Syntax.LeafBlock(null);
    private sealed class UnknownContainer() : Markdig.Syntax.ContainerBlock(null);

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

    [Fact]
    public void PairwiseSpacing_CoversEveryBlockKindAndConsultsExactTrivia()
    {
        var spacing = new MarkdownSpacing { ParagraphGap = 3, HeadingTopGap = 4, HeadingBottomGap = 5 };
        var kinds = Enum.GetValues<MarkdownBlockKind>();
        foreach (var previousKind in kinds)
        foreach (var currentKind in kinds)
        {
            var previous = new MarkdownTopLevelBlock(0, 1, previousKind, 0);
            var current = new MarkdownTopLevelBlock(3, 4, currentKind, 1);
            var withoutBlank = MarkdownLayoutEngine.GetSeparatorRows(previous, current, spacing, "a\nb");
            var withBlank = MarkdownLayoutEngine.GetSeparatorRows(previous, current, spacing, "a\n\nb");

            Assert.Equal(0, withoutBlank);
            var expected = ExpectedPairwiseGap(previousKind, currentKind, spacing);
            Assert.Equal(expected, withBlank);
        }
    }

    private static int ExpectedPairwiseGap(MarkdownBlockKind previous, MarkdownBlockKind current, MarkdownSpacing spacing)
    {
        if (previous is MarkdownBlockKind.Html or MarkdownBlockKind.ThematicBreak or MarkdownBlockKind.Other ||
            current is MarkdownBlockKind.Html or MarkdownBlockKind.ThematicBreak or MarkdownBlockKind.Other) return 0;
        if (current == MarkdownBlockKind.Heading) return spacing.HeadingTopGap;
        if (previous == MarkdownBlockKind.Heading) return spacing.HeadingBottomGap;
        if (previous == current && previous is MarkdownBlockKind.List or MarkdownBlockKind.Quote) return 0;
        return spacing.ParagraphGap;
    }

    [Fact]
    public void RawLayout_PreservesCanonicalInterBlockTriviaInsteadOfSemanticSpacing()
    {
        const string source = "# heading\r\n\r\nparagraph";
        var document = new MarkdownDocumentParser().Parse(source,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var layout = new MarkdownLayoutEngine().Layout(document,
            new(40, MarkdownTheme.FromTheme(Theme.Default), Mode: MarkdownPresentationMode.Raw));

        Assert.Equal(["# heading", "", "paragraph"], layout.Rows.Select(RowText));
        Assert.DoesNotContain(layout.Rows, static row => row.Kind == MarkdownLayoutRowKind.Separator);
    }

    [Fact]
    public void RawLayout_PreservesPreciseUtf16MappingsAcrossCrlfWrappingAndSanitization()
    {
        const string source = "ab\r\n界c\u001b";
        var layout = new MarkdownLayoutEngine().LayoutRaw(source, "test-pipeline",
            new(2, MarkdownTheme.FromTheme(Theme.Default), Mode: MarkdownPresentationMode.Raw));
        var runs = layout.Rows.SelectMany(static row => row.Line.Runs)
            .Where(static run => !run.IsDecorative).ToArray();

        Assert.Contains(runs, static run => run.Text == "ab" && run.SourceStart == 0 && run.SourceEndExclusive == 2);
        Assert.Contains(runs, static run => run.Text == "界" && run.SourceStart == 4 && run.SourceEndExclusive == 5);
        Assert.Contains(runs, static run => run.Text == "c�" && run.SourceStart == 5 && run.SourceEndExclusive == 7);
    }

    [Fact]
    public void TransformedInlineContentRetainsExplicitCanonicalSegments()
    {
        const string source = "escaped \\* and &amp; `code` <https://example.com>\nsoft\nbreak";
        var document = new MarkdownDocumentParser().Parse(source,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var layout = new MarkdownLayoutEngine().Layout(document,
            new(80, MarkdownTheme.FromTheme(Theme.Default)));
        var runs = layout.Rows.SelectMany(static row => row.Line.Runs).ToArray();

        var escaped = Assert.Single(runs.Where(static run => run.Text.Contains('*')));
        var escapedMap = Assert.Single(escaped.SourceMap.Where(segment =>
            escaped.Text[segment.VisualStart..segment.VisualEndExclusive] == "*"));
        Assert.Equal("\\*", source[escapedMap.SourceStart..escapedMap.SourceEndExclusive]);
        var code = Assert.Single(runs.Where(static run => run.Text == "code"));
        Assert.Equal("code", source[code.SourceStart!.Value..code.SourceEndExclusive!.Value]);
        var autolink = Assert.Single(runs.Where(static run => run.Text == "https://example.com"));
        Assert.Equal("https://example.com", source[autolink.SourceStart!.Value..autolink.SourceEndExclusive!.Value]);
        Assert.All(runs.Where(static run => run.Text == " "), static run =>
            Assert.True(run.SourceStart.HasValue == run.SourceEndExclusive.HasValue));
        Assert.All(runs.Where(static run => !run.IsDecorative && run.SourceStart.HasValue), static run =>
            Assert.False(run.SourceMap.IsDefaultOrEmpty));
    }

    [Fact]
    public void ResourceLimitsSelectDeterministicDegradationWithoutChangingCanonicalSource()
    {
        const string source = "one\ntwo\nthree";
        var document = new MarkdownDocumentParser().Parse(source,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var engine = new MarkdownLayoutEngine();
        var theme = MarkdownTheme.FromTheme(Theme.Default);

        var sourceLimited = engine.Layout(document, new(20, theme, ResourceLimits: new()
        {
            MaximumRichSourceLength = 4
        }));
        var rowLimited = engine.LayoutRaw(source, document.PipelineId, new(20, theme,
            Mode: MarkdownPresentationMode.Raw, ResourceLimits: new() { MaximumLayoutRows = 2 }));

        Assert.Equal(MarkdownDegradationReason.SourceLength, sourceLimited.DegradationReason);
        Assert.Equal(MarkdownDegradationReason.LayoutRows, rowLimited.DegradationReason);
        Assert.Equal(source, document.Source);
        Assert.Equal(source, string.Join('\n', sourceLimited.Rows.Select(RowText)));
        Assert.Equal(["one", "two"], rowLimited.Rows.Select(RowText));
        Assert.True(rowLimited.HasMoreSource);
        var nextPage = engine.LayoutRawPage(source, document.PipelineId, new(20, theme,
            Mode: MarkdownPresentationMode.Raw, ResourceLimits: new() { MaximumLayoutRows = 2 }),
            rowLimited.NextSourceOffset!.Value);
        Assert.Equal("three", Assert.Single(nextPage.Rows).Line.Runs[0].Text);
        Assert.False(nextPage.HasMoreSource);
    }

    [Fact]
    public void TableAndHighlightLimitsUseReadableDeterministicFallbacks()
    {
        const string source = "| a | b |\n|---|---|\n| 1 | 2 |\n\n```csharp\nvar value = 42;\n```";
        var document = new MarkdownDocumentParser().Parse(source,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var limits = new MarkdownResourceLimits
        {
            MaximumTableColumns = 1,
            MaximumHighlightedCodeLength = 4
        };
        var layout = new MarkdownLayoutEngine().Layout(document,
            new(80, MarkdownTheme.FromTheme(Theme.Default), ResourceLimits: limits));

        Assert.Equal(MarkdownDegradationReason.TableShape, layout.DegradationReason);
        Assert.Contains("| a | b |", string.Join('\n', layout.Rows.Select(RowText)), StringComparison.Ordinal);
        Assert.Contains(layout.Blocks, static block => block.DegradationReason == MarkdownDegradationReason.CodeHighlightLength);
    }

    [Fact]
    public void CodeHighlightAndTableWrappingPreserveExactSourceSegments()
    {
        const string source = "| Value |\n|---|\n| [**bold** and \\*star](https://example.com) |\n\n```csharp\nvar x = 42;\nreturn x;\n```";
        var document = new MarkdownDocumentParser().Parse(source,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var layout = new MarkdownLayoutEngine().Layout(document,
            new(18, MarkdownTheme.FromTheme(Theme.Default)));
        var runs = layout.Rows.SelectMany(static row => row.Line.Runs).ToArray();

        foreach (var expected in new[] { "var", "42", "return" })
        {
            var matching = runs.Where(run => run.Text == expected).ToArray();
            Assert.True(matching.Length > 0, $"Missing '{expected}' in: {string.Join('|', runs.Select(static run => run.Text))}");
            Assert.All(matching, run =>
            {
                Assert.Equal(expected, source[run.SourceStart!.Value..run.SourceEndExclusive!.Value]);
                Assert.All(run.SourceMap, segment => Assert.Equal(
                    run.Text[segment.VisualStart..segment.VisualEndExclusive],
                    source[segment.SourceStart..segment.SourceEndExclusive]));
            });
        }
        var codeStart = source.IndexOf("var x", StringComparison.Ordinal);
        foreach (var run in runs.Where(run => !run.IsDecorative && run.SourceStart >= codeStart))
            Assert.All(run.SourceMap, segment => Assert.Equal(
                run.Text[segment.VisualStart..segment.VisualEndExclusive],
                source[segment.SourceStart..segment.SourceEndExclusive]));
        var tableStar = Assert.Single(runs.Where(static run => run.Text.Contains("star", StringComparison.Ordinal)));
        Assert.Contains(tableStar.SourceMap, segment =>
            tableStar.Text[segment.VisualStart..segment.VisualEndExclusive] == "*" &&
            source[segment.SourceStart..segment.SourceEndExclusive] == "\\*");
    }

    [Fact]
    public void TrimmingVisualWhitespaceAlsoTrimsExactSourceMapExtent()
    {
        const string source = "abc   ";
        var layout = new MarkdownLayoutEngine().LayoutRaw(source, "trim-test",
            new(20, MarkdownTheme.FromTheme(Theme.Default), Mode: MarkdownPresentationMode.Raw));
        var run = Assert.Single(Assert.Single(layout.Rows).Line.Runs);

        Assert.Equal("abc", run.Text);
        Assert.Equal(3, run.SourceEndExclusive);
        Assert.All(run.SourceMap, segment => Assert.InRange(segment.VisualEndExclusive, 0, run.Text.Length));
    }

    [Fact]
    public void TransformedHighlighterAndCollapsedTableWhitespaceRetainExactSourceSegments()
    {
        const string codeSource = "```text\r\na\tb\r\n```";
        var parser = new MarkdownDocumentParser();
        var codeDocument = parser.Parse(codeSource,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var codeLayout = new MarkdownLayoutEngine(new TransformingHighlighter()).Layout(codeDocument,
            new(40, MarkdownTheme.FromTheme(Theme.Default)));
        var transformed = Assert.Single(codeLayout.Rows.SelectMany(static row => row.Line.Runs),
            static run => run.Text == "A⇥B");
        Assert.Equal("a", codeSource[transformed.SourceMap[0].SourceStart..transformed.SourceMap[0].SourceEndExclusive]);
        Assert.Equal("\t", codeSource[transformed.SourceMap[1].SourceStart..transformed.SourceMap[1].SourceEndExclusive]);
        Assert.Equal("b", codeSource[transformed.SourceMap[2].SourceStart..transformed.SourceMap[2].SourceEndExclusive]);

        const string tableSource = "| value |\n|---|\n| a   b |";
        var table = parser.Parse(tableSource,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var tableLayout = new MarkdownLayoutEngine().Layout(table,
            new(30, MarkdownTheme.FromTheme(Theme.Default)));
        var collapsed = tableLayout.Rows.SelectMany(static row => row.Line.Runs)
            .SelectMany(run => run.SourceMap.Select(map => (run, map)))
            .Single(pair => pair.run.Text[pair.map.VisualStart..pair.map.VisualEndExclusive] == " " &&
                tableSource[pair.map.SourceStart..pair.map.SourceEndExclusive] == "   ");
        Assert.Equal("   ", tableSource[collapsed.map.SourceStart..collapsed.map.SourceEndExclusive]);
    }

    [Fact]
    public void ParserRejectsOversizeAndDelimiterAdversarialWorkBeforeSemanticParsing()
    {
        var parser = new MarkdownDocumentParser();
        var pipeline = MarkdownPipelineFactory.CreateDefault();
        Assert.Throws<ArgumentException>(() => parser.Parse("12345",
            new MarkdownParseOptions { Pipeline = pipeline, MaximumSourceLength = 4 }));
        Assert.Throws<ArgumentException>(() => parser.Parse("*****",
            new MarkdownParseOptions { Pipeline = pipeline, MaximumDelimiterCharacters = 4 }));
    }

    [Fact]
    public void TableCells_PreserveTypedInlineOrderStylesAndHyperlinks()
    {
        const string source = "| Value |\n|---|\n| [**bold** and `code`](https://example.com) |";
        var document = new MarkdownDocumentParser().Parse(source,
            new MarkdownParseOptions { Pipeline = MarkdownPipelineFactory.CreateDefault() });
        var layout = new MarkdownLayoutEngine().Layout(document,
            new(60, MarkdownTheme.FromTheme(Theme.Default)));
        var runs = layout.Rows.SelectMany(static row => row.Line.Runs).ToArray();

        Assert.True(Array.FindIndex(runs, static run => run.Text.Contains("bold", StringComparison.Ordinal)) <
                    Array.FindIndex(runs, static run => run.Text.Contains("code", StringComparison.Ordinal)));
        Assert.Contains(runs, static run => run.Text.Contains("bold", StringComparison.Ordinal) &&
            run.Style.Attributes.HasFlag(TextAttributes.Bold) && run.Hyperlink is not null);
        Assert.Contains(runs, static run => run.Text.Contains("code", StringComparison.Ordinal) && run.Hyperlink is not null);
    }

    [Fact]
    public void WidthOneGrid_ReplacesUnrepresentableWideGraphemeWithoutOverrun()
    {
        using var grid = new TerminalGrid(1, 1);

        Assert.True(grid.Write("界", Style.Default));
        Assert.Equal("�", grid.GetGrapheme(grid.GetCell(0, 0)).ToString());
    }

    [Fact]
    public void MarkdownTheme_HasOnlyFactoryDerivedStructuralIdentity()
    {
        Assert.Empty(typeof(MarkdownTheme).GetConstructors());
        Assert.False(typeof(MarkdownTheme).GetProperty(nameof(MarkdownTheme.ThemeKey))!.CanWrite);
    }

    private static string RowText(MarkdownLayoutRow row) => string.Concat(row.Line.Runs.Select(static run => run.Text));

    private sealed class TransformingHighlighter : ICodeHighlighter
    {
        public CodeHighlightResult Highlight(ReadOnlyMemory<char> source, string? language, MarkdownTheme theme) =>
            new([new StyledTerminalLine([new StyledTerminalRun("A⇥B", theme.Body, SourceMap:
                [new(0, 1, 0, 1), new(1, 2, 1, 2), new(2, 3, 2, 3)])])], "text");
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

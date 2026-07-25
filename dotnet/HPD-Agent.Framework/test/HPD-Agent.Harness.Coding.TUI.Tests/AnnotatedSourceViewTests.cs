using HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;
using HPD.TUI.Rendering;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class AnnotatedSourceViewTests
{
    [Fact]
    public void Render_MultipleAnnotationsUseOneAlignedGutter()
    {
        var rendered = Render(new(
            "Program.cs",
            "csharp",
            [
                new([
                    Line(17, "foreach (var price in prices)", []),
                    Line(18, "total += price;", [
                        new("▶", SourceAnnotationTone.Current),
                        new("●", SourceAnnotationTone.Success)
                    ], emphasis: SourceLineEmphasis.Current),
                    Line(19, "}", [])
                ])
            ]));

        rendered.Should().Contain("17    foreach");
        rendered.Should().Contain("18 ▶● total += price;");
        rendered.Should().Contain("19    }");
    }

    [Fact]
    public void Render_TrailingConditionMovesToContinuationAtNarrowWidth()
    {
        var rendered = Render(new(
            "Program.cs",
            "csharp",
            [
                new([
                    Line(
                        27,
                        "total -= discount;",
                        [new("◆", SourceAnnotationTone.Information)],
                        "when price < 0")
                ])
            ]),
            width: 24);

        var lines = rendered.Split('\n');
        lines.Should().HaveCountGreaterThan(1);
        lines[0].Should().Contain("27 ◆ total -= discount;");
        rendered.Should().Contain("when price < 0");
        rendered.Count(static character => character == '◆').Should().Be(1);
    }

    [Fact]
    public void Render_WideUnicodeWrapsWithoutRepeatingLineNumberOrMarker()
    {
        var rendered = Render(new(
            "unicode.cs",
            "csharp",
            [
                new([
                    Line(
                        120,
                        "var 結果 = \"非常に長い値\";",
                        [new("●", SourceAnnotationTone.Success)])
                ])
            ]),
            width: 20);

        rendered.Should().Contain("120 ●");
        rendered.Count(static character => character == '●').Should().Be(1);
        rendered.Split('\n').Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Render_ExpandsTabsAtStableFourColumnStops()
    {
        var rendered = Render(new(
            "Program.cs",
            "csharp",
            [
                new([
                    Line(22, "\tvar destination = value;", []),
                    Line(23, "\t\treturn destination;", [
                        new("●", SourceAnnotationTone.Success)
                    ], emphasis: SourceLineEmphasis.Subtle)
                ])
            ]));

        rendered.Should().Contain("22       var destination = value;");
        rendered.Should().Contain("23 ●         return destination;");
        rendered.Should().NotContain("\t");
    }

    [Fact]
    public void Render_WrappedAnnotationDoesNotExtendSourceHighlightBand()
    {
        var ansi = RenderAnsi(new(
            "Program.cs",
            "csharp",
            [
                new([
                    Line(
                        23,
                        "\tvar status = left.TryMul(",
                        [new("○", SourceAnnotationTone.Warning)],
                        "added · pending until debugging starts",
                        SourceLineEmphasis.Warning)
                ])
            ]),
            width: 34);

        var rows = ansi.Split('\n');
        rows.Should().HaveCountGreaterThan(1);
        rows[^1].Should().NotContain("48;2;72;58;24");
    }

    [Fact]
    public void Render_HighlightBandIncludesLineNumberAndMarkerGutter()
    {
        var row = RenderAnsi(new(
            "Program.cs",
            "csharp",
            [
                new([
                    Line(
                        21,
                        "        var value = 42;",
                        [new("●", SourceAnnotationTone.Success)],
                        emphasis: SourceLineEmphasis.Subtle)
                ])
            ])).Split('\n')[0];

        var background = row.IndexOf("48;2;32;34;40", StringComparison.Ordinal);
        var lineNumber = row.IndexOf("21", StringComparison.Ordinal);
        background.Should().BeGreaterThanOrEqualTo(0);
        lineNumber.Should().BeGreaterThan(background);
    }

    [Fact]
    public void Render_TruncationIsExplicit()
    {
        var rendered = Render(new(
            "Program.cs",
            "csharp",
            [new([Line(18, "total += price;", [])])],
            Truncated: true,
            TruncationReason: "additional breakpoint context omitted"));

        rendered.Should().Contain("⋮ additional breakpoint context omitted");
    }

    [Fact]
    public void Render_KnownLanguageHighlightsKeywordStringAndNumber()
    {
        var ansi = RenderAnsi(new(
            "Program.cs",
            "csharp",
            [
                new([
                    Line(1, "public int Count = 42; string Name = \"item\";", [])
                ])
            ]));

        ansi.Should().Contain("38;2;120;170;255");
        ansi.Should().Contain("38;2;210;175;95");
        ansi.Should().Contain("38;2;120;190;140");
    }

    [Theory]
    [InlineData("Program.cs", "csharp")]
    [InlineData("worker.py", "python")]
    [InlineData("main.go", "go")]
    [InlineData("Cargo.toml", "toml")]
    [InlineData("README", null)]
    public void LanguageClassifier_UsesPathExtension(string path, string? expected)
        => SourceLanguageClassifier.FromPath(path).Should().Be(expected);

    private static AnnotatedSourceLine Line(
        int number,
        string text,
        IReadOnlyList<SourceAnnotation> annotations,
        string? trailing = null,
        SourceLineEmphasis emphasis = SourceLineEmphasis.None)
        => new(number, text, annotations, trailing, emphasis);

    private static string Render(
        AnnotatedSourceDocument document,
        int width = 80,
        int height = 30)
        => TuiCapture.RenderToString(
            new AnnotatedSourceView(document, CodingHarnessTuiTheme.Default),
            width,
            height,
            trimTrailingBlankLines: true);

    private static string RenderAnsi(
        AnnotatedSourceDocument document,
        int width = 80,
        int height = 30)
        => TuiCapture.RenderToAnsi(
            new AnnotatedSourceView(document, CodingHarnessTuiTheme.Default),
            width,
            height);
}

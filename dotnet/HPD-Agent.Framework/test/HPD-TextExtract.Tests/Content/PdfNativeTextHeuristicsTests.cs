using HPD.TextExtract.Models;
using HPD.TextExtract.Pdf;

namespace HPD.TextExtract.Tests.Content;

public sealed class PdfNativeTextHeuristicsTests
{
    [Fact]
    public void Deduplicate_DropsEarlierExactDuplicate()
    {
        var items = new List<PdfTextItem>
        {
            Item("hello", 0, 0, 10, 5),
            Item("hello", 1, 0, 10, 5)
        };

        PdfNativeTextHeuristics.Deduplicate(items);

        var item = Assert.Single(items);
        Assert.Equal(1, item.BoundingBox.X);
    }

    [Fact]
    public void Deduplicate_KeepsNonOverlappingItems()
    {
        var items = new List<PdfTextItem>
        {
            Item("a", 0, 0, 5, 5),
            Item("b", 100, 100, 5, 5)
        };

        PdfNativeTextHeuristics.Deduplicate(items);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Deduplicate_DropsEarlierDifferentTextWhenOverlapIsHeavy()
    {
        var items = new List<PdfTextItem>
        {
            Item("old", 0, 0, 10, 5),
            Item("new", 0, 0, 10, 5)
        };

        PdfNativeTextHeuristics.Deduplicate(items);

        var item = Assert.Single(items);
        Assert.Equal("new", item.Text);
    }

    [Fact]
    public void Deduplicate_KeepsDifferentTextWhenOverlapIsLight()
    {
        var items = new List<PdfTextItem>
        {
            Item("aaa", 0, 0, 10, 5),
            Item("bbb", 9, 0, 10, 5)
        };

        PdfNativeTextHeuristics.Deduplicate(items);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Deduplicate_NoopsForEmptyOrSingle()
    {
        var empty = new List<PdfTextItem>();
        PdfNativeTextHeuristics.Deduplicate(empty);
        Assert.Empty(empty);

        var one = new List<PdfTextItem> { Item("x", 0, 0, 1, 1) };
        PdfNativeTextHeuristics.Deduplicate(one);
        Assert.Single(one);
    }

    [Fact]
    public void AdjustRotation_AppliesPageRotationAndWraps()
    {
        Assert.Equal(28.64789f, PdfNativeTextHeuristics.AdjustRotation(0.5f, 0), precision: 5);

        var rotated180 = PdfNativeTextHeuristics.AdjustRotation(MathF.PI, 180);
        Assert.True(rotated180 < 0.001f || Math.Abs(rotated180 - 360) < 0.001f);

        var rotated90 = PdfNativeTextHeuristics.AdjustRotation(0, 90);
        Assert.True(rotated90 is >= 0 and < 360);
        Assert.Equal(90, rotated90, precision: 6);
    }

    [Fact]
    public void FontAndCodepointSignals_FlagKnownCorruptPatterns()
    {
        Assert.True(PdfNativeTextHeuristics.IsBuggyFontName("TTFoo"));
        Assert.True(PdfNativeTextHeuristics.IsBuggyFontName("ABCDEF+TTBar"));
        Assert.True(PdfNativeTextHeuristics.IsBuggyFontName("ABCDEF_Foo"));
        Assert.False(PdfNativeTextHeuristics.IsBuggyFontName("Arial"));

        Assert.True(PdfNativeTextHeuristics.IsBuggyCodepoint(0x00));
        Assert.True(PdfNativeTextHeuristics.IsBuggyCodepoint(0x1F));
        Assert.False(PdfNativeTextHeuristics.IsBuggyCodepoint(0x20));
        Assert.True(PdfNativeTextHeuristics.IsBuggyCodepoint(0xE001));
        Assert.True(PdfNativeTextHeuristics.IsBuggyCodepoint(0xF8FF));
        Assert.False(PdfNativeTextHeuristics.IsBuggyCodepoint(0xE000));
        Assert.False(PdfNativeTextHeuristics.IsBuggyCodepoint(0xF900));
    }

    [Fact]
    public void IsValidUnicodeScalar_RejectsPdfiumInvalidSurrogateCodepoints()
    {
        Assert.False(PdfNativeTextHeuristics.IsValidUnicodeScalar(0));
        Assert.False(PdfNativeTextHeuristics.IsValidUnicodeScalar(0xD800));
        Assert.False(PdfNativeTextHeuristics.IsValidUnicodeScalar(0xDFFF));
        Assert.False(PdfNativeTextHeuristics.IsValidUnicodeScalar(0x110000));
        Assert.True(PdfNativeTextHeuristics.IsValidUnicodeScalar('A'));
        Assert.True(PdfNativeTextHeuristics.IsValidUnicodeScalar(0x1F600));
    }

    [Fact]
    public void ColorAndRenderModeHelpers_NormalizePdfiumSignals()
    {
        Assert.Equal("#12ABCDEF", PdfNativeTextHeuristics.ToArgb(0x12, 0xAB, 0xCD, 0xEF));
        Assert.Equal("#00000000", PdfNativeTextHeuristics.ToArgb(0, 0, 0, 0));
        Assert.True(PdfNativeTextHeuristics.IsInvisibleRenderMode("FPDF_TEXTRENDERMODE_INVISIBLE"));
        Assert.False(PdfNativeTextHeuristics.IsInvisibleRenderMode("FPDF_TEXTRENDERMODE_FILL"));
    }

    [Fact]
    public void ExpandPdfGlyph_ExpandsPdfiumLigatureAndFallbackCodes()
    {
        Assert.Equal("-", PdfNativeTextHeuristics.ExpandPdfGlyph(0x02));
        Assert.Equal("ff", PdfNativeTextHeuristics.ExpandPdfGlyph(0x1A));
        Assert.Equal("ft", PdfNativeTextHeuristics.ExpandPdfGlyph(0x1B));
        Assert.Equal("fi", PdfNativeTextHeuristics.ExpandPdfGlyph(0x1C));
        Assert.Equal("Th", PdfNativeTextHeuristics.ExpandPdfGlyph(0x1D));
        Assert.Equal("ffi", PdfNativeTextHeuristics.ExpandPdfGlyph(0x1E));
        Assert.Equal("fl", PdfNativeTextHeuristics.ExpandPdfGlyph(0x1F));
        Assert.Equal("A", PdfNativeTextHeuristics.ExpandPdfGlyph('A'));
    }

    [Fact]
    public void PdfiumGeometry_ToBoundingBox_NormalizesTransformedCorners()
    {
        var transform = new PdfiumViewportTransform(-1, 0, 0, 1, 100, 0);

        var box = PdfiumGeometry.ToBoundingBox(transform, left: 10, bottom: 20, right: 30, top: 50);

        Assert.Equal(new BoundingBox(70, 20, 20, 30), box);
    }

    private static PdfTextItem Item(string text, float x, float y, float width, float height) => new()
    {
        Text = text,
        BoundingBox = new BoundingBox(x, y, width, height)
    };
}

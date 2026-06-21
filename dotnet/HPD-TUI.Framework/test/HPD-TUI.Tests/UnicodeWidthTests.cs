using HPD.TUI.Utilities;

namespace HPD.TUI.Tests;

public sealed class UnicodeWidthTests
{
    [Fact]
    public void GetWidth_ReturnsOneForAscii()
    {
        Assert.Equal(1, UnicodeWidth.GetWidth(new Rune('A')));
    }

    [Fact]
    public void GetWidth_ReturnsTwoForCjk()
    {
        Assert.Equal(2, UnicodeWidth.GetWidth(new Rune(0x4E00)));
    }

    [Fact]
    public void GetWidth_ReturnsTwoForEmoji()
    {
        Assert.Equal(2, UnicodeWidth.GetWidth(new Rune(0x1F600)));
    }

    [Fact]
    public void GetWidth_ReturnsZeroForCombiningMarks()
    {
        Assert.Equal(0, UnicodeWidth.GetWidth(new Rune(0x0301)));
    }

    [Fact]
    public void GetWidth_SumsUtf16SurrogatePairsAsOneRune()
    {
        Assert.Equal(4, UnicodeWidth.GetWidth("A😀B"));
    }
}

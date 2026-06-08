using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class KeyParserTests
{
    [Fact]
    public void Parse_RecognizesArrowKeys()
    {
        Assert.Equal(KeyCode.UpArrow, KeyParser.Parse("\x1b[A").Key);
        Assert.Equal(KeyCode.DownArrow, KeyParser.Parse("\x1b[B").Key);
    }

    [Fact]
    public void Parse_RecognizesUnicodeCharacters()
    {
        var key = KeyParser.Parse("😀");

        Assert.Equal(KeyCode.Character, key.Key);
        Assert.Equal(new Rune(0x1F600), key.Character);
    }

    [Fact]
    public void Parse_UnpairedSurrogateReturnsUnknown()
    {
        var key = KeyParser.Parse("\ud83d");

        Assert.Equal(KeyCode.Unknown, key.Key);
    }
}

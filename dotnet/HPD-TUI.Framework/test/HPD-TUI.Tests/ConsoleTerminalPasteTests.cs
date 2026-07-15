using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class ConsoleTerminalPasteTests
{
    [Fact]
    public void TryClassifyBurstPaste_TreatsMultilineBurstAsPaste()
    {
        var keys = ToConsoleKeys("first line\nsecond line");

        var isPaste = ConsoleTerminal.TryClassifyBurstPaste(keys, out var text, out var fallbackKeys);

        Assert.True(isPaste);
        Assert.Equal("first line\nsecond line", text);
        Assert.Empty(fallbackKeys);
    }

    [Fact]
    public void TryClassifyBurstPaste_TreatsLongSingleLineBurstAsPaste()
    {
        var keys = ToConsoleKeys("pasted text");

        var isPaste = ConsoleTerminal.TryClassifyBurstPaste(keys, out var text, out var fallbackKeys);

        Assert.True(isPaste);
        Assert.Equal("pasted text", text);
        Assert.Empty(fallbackKeys);
    }

    [Fact]
    public void TryClassifyBurstPaste_ReplaysShortTypingAsKeys()
    {
        var keys = ToConsoleKeys("hi");

        var isPaste = ConsoleTerminal.TryClassifyBurstPaste(keys, out var text, out var fallbackKeys);

        Assert.False(isPaste);
        Assert.Equal("", text);
        Assert.Collection(
            fallbackKeys,
            key =>
            {
                Assert.Equal(KeyCode.Character, key.Key);
                Assert.Equal(new Rune('h'), key.Character);
            },
            key =>
            {
                Assert.Equal(KeyCode.Character, key.Key);
                Assert.Equal(new Rune('i'), key.Character);
            });
    }

    [Fact]
    public void TryClassifyBurstPaste_ReplaysSingleEnterAsKey()
    {
        var keys = ToConsoleKeys("\n");

        var isPaste = ConsoleTerminal.TryClassifyBurstPaste(keys, out var text, out var fallbackKeys);

        Assert.False(isPaste);
        Assert.Equal("", text);
        Assert.Single(fallbackKeys);
        Assert.Equal(KeyCode.Enter, fallbackKeys[0].Key);
    }

    [Fact]
    public void TryClassifyBurstPaste_ReplaysWhitespaceOnlyLineBreaksAsKeys()
    {
        var keys = ToConsoleKeys("\n\n");

        var isPaste = ConsoleTerminal.TryClassifyBurstPaste(keys, out var text, out var fallbackKeys);

        Assert.False(isPaste);
        Assert.Equal("", text);
        Assert.Equal(2, fallbackKeys.Count);
        Assert.All(fallbackKeys, key => Assert.Equal(KeyCode.Enter, key.Key));
    }

    [Fact]
    public void TryClassifyBurstPaste_ReplaysControlModifiedKeys()
    {
        var keys = new[]
        {
            new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: true)
        };

        var isPaste = ConsoleTerminal.TryClassifyBurstPaste(keys, out var text, out var fallbackKeys);

        Assert.False(isPaste);
        Assert.Equal("", text);
        Assert.Single(fallbackKeys);
        Assert.Equal(KeyModifiers.Ctrl, fallbackKeys[0].Modifiers);
    }

    private static ConsoleKeyInfo[] ToConsoleKeys(string text) =>
        text.Select(ch => new ConsoleKeyInfo(ch, ToConsoleKey(ch), shift: false, alt: false, control: false))
            .ToArray();

    private static ConsoleKey ToConsoleKey(char ch) =>
        ch switch
        {
            '\n' => ConsoleKey.Enter,
            '\r' => ConsoleKey.Enter,
            '\t' => ConsoleKey.Tab,
            >= 'a' and <= 'z' => ConsoleKey.A + (ch - 'a'),
            >= 'A' and <= 'Z' => ConsoleKey.A + (ch - 'A'),
            >= '0' and <= '9' => ConsoleKey.D0 + (ch - '0'),
            ' ' => ConsoleKey.Spacebar,
            _ => ConsoleKey.NoName
        };
}

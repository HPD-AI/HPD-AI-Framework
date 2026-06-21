using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Terminal;

public static class KeyParser
{
    public static KeyEvent Parse(ReadOnlySpan<char> data)
    {
        if (data.IsEmpty)
        {
            return default;
        }

        if (data[0] == '\x1b')
        {
            if (data.SequenceEqual("\x1b[A")) return new KeyEvent(KeyCode.UpArrow);
            if (data.SequenceEqual("\x1b[B")) return new KeyEvent(KeyCode.DownArrow);
            if (data.SequenceEqual("\x1b[C")) return new KeyEvent(KeyCode.RightArrow);
            if (data.SequenceEqual("\x1b[D")) return new KeyEvent(KeyCode.LeftArrow);
            if (data.SequenceEqual("\x1b[H")) return new KeyEvent(KeyCode.Home);
            if (data.SequenceEqual("\x1b[F")) return new KeyEvent(KeyCode.End);
            if (data.SequenceEqual("\x1b[3~")) return new KeyEvent(KeyCode.Delete);
            if (data.SequenceEqual("\x1b[5~")) return new KeyEvent(KeyCode.PageUp);
            if (data.SequenceEqual("\x1b[6~")) return new KeyEvent(KeyCode.PageDown);

            return data.Length == 1 ? new KeyEvent(KeyCode.Escape) : new KeyEvent(KeyCode.Unknown);
        }

        if (data.Length == 1)
        {
            return data[0] switch
            {
                '\r' or '\n' => new KeyEvent(KeyCode.Enter),
                '\x7f' or '\b' => new KeyEvent(KeyCode.Backspace),
                '\t' => new KeyEvent(KeyCode.Tab),
                _ when data[0] < ' ' => new KeyEvent(KeyCode.Unknown, default, KeyModifiers.Ctrl),
                _ when char.IsSurrogate(data[0]) => new KeyEvent(KeyCode.Unknown),
                _ => new KeyEvent(KeyCode.Character, new Rune(data[0]))
            };
        }

        var enumerator = new RuneEnumerator(data);
        return enumerator.MoveNext()
            ? new KeyEvent(KeyCode.Character, enumerator.Current)
            : default;
    }
}

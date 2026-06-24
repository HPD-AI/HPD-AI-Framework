using HPD.TUI.Core;
using System.Text;

namespace HPD.TUI.Terminal;

public class ConsoleTerminal : ITerminal, ITerminalInput
{
    private const string BracketedPasteStart = "[200~";
    private const string BracketedPasteEnd = "\x1b[201~";

    public ITerminalInput Input => this;

    public TerminalSize GetSize()
    {
        var width = Math.Max(1, Console.WindowWidth);
        var height = Math.Max(1, Console.WindowHeight);
        return new TerminalSize(width, height);
    }

    public void Write(ReadOnlySpan<char> text)
    {
        Console.Out.Write(text);
    }

    public void Flush()
    {
        Console.Out.Flush();
    }

    public async ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadInput(out var input))
            {
                return input;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TryReadInput(out TerminalInputEvent input)
    {
        input = default;

        if (Console.IsInputRedirected || !Console.KeyAvailable)
        {
            return false;
        }

        var info = Console.ReadKey(intercept: true);
        var fallback = default(KeyEvent);
        if (info.Key == ConsoleKey.Escape && TryReadBracketedPaste(out var pasted, out fallback))
        {
            input = TerminalInputEvent.FromPaste(pasted);
            return true;
        }

        if (fallback.Key != KeyCode.Unknown)
        {
            input = TerminalInputEvent.FromKey(fallback);
            return true;
        }

        input = TerminalInputEvent.FromKey(ConsoleKeyMapper.Map(info));
        return true;
    }

    public void HideCursor()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Out.Write("\x1b[?2004h");
            Console.CursorVisible = false;
        }
    }

    public void ShowCursor()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Out.Write("\x1b[?2004l");
            Console.CursorVisible = true;
        }
    }

    public void Dispose()
    {
        ShowCursor();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private bool TryReadBracketedPaste(out string text, out KeyEvent fallback)
    {
        text = "";
        fallback = default;
        if (!TryReadExact(BracketedPasteStart, out var consumed))
        {
            fallback = KeyParser.Parse("\x1b" + consumed);
            return false;
        }

        var builder = new StringBuilder();
        while (true)
        {
            var ch = ReadCharWithBriefWait();
            if (ch is null)
            {
                text = builder.ToString();
                return true;
            }

            builder.Append(ch.Value);
            if (EndsWith(builder, BracketedPasteEnd))
            {
                builder.Length -= BracketedPasteEnd.Length;
                text = builder.ToString();
                return true;
            }
        }
    }

    private static bool TryReadExact(string expected, out string consumed)
    {
        var builder = new StringBuilder();
        foreach (var ch in expected)
        {
            if (!WaitForInput())
            {
                consumed = builder.ToString();
                return false;
            }

            var actual = Console.ReadKey(intercept: true).KeyChar;
            builder.Append(actual);
            if (actual != ch)
            {
                consumed = builder.ToString();
                return false;
            }
        }

        consumed = builder.ToString();
        return true;
    }

    private static char? ReadCharWithBriefWait()
    {
        return WaitForInput()
            ? Console.ReadKey(intercept: true).KeyChar
            : null;
    }

    private static bool WaitForInput()
    {
        for (var i = 0; i < 50; i++)
        {
            if (Console.KeyAvailable)
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }

    private static bool EndsWith(StringBuilder builder, string value)
    {
        if (builder.Length < value.Length)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (builder[builder.Length - value.Length + i] != value[i])
            {
                return false;
            }
        }

        return true;
    }
}

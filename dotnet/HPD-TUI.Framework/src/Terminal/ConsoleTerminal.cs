using HPD.TUI.Core;
using System.Text;

namespace HPD.TUI.Terminal;

public class ConsoleTerminal : ITerminal, ITerminalInput
{
    private const string BracketedPasteStart = "[200~";
    private const string BracketedPasteEnd = "\x1b[201~";
    private const int InputWaitAttempts = 50;
    private const int PastePayloadWaitAttempts = 250;
    private const int BurstPasteWaitAttempts = 2;
    private const int BurstPasteMinimumLength = 8;
    private readonly Queue<KeyEvent> _pendingKeys = new();
    private TerminalSize _lastObservedSize;

    public ConsoleTerminal()
    {
        _lastObservedSize = GetSize();
    }

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
            var size = GetSize();
            if (size != _lastObservedSize)
            {
                _lastObservedSize = size;
                return TerminalInputEvent.FromResize(size);
            }

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

        if (_pendingKeys.Count > 0)
        {
            input = TerminalInputEvent.FromKey(_pendingKeys.Dequeue());
            return true;
        }

        if (Console.IsInputRedirected || !Console.KeyAvailable)
        {
            return false;
        }

        var info = Console.ReadKey(intercept: true);
        var fallback = default(KeyEvent);
        if (info.Key == ConsoleKey.Escape && TryReadBracketedPaste(out var pasted, out fallback))
        {
            if (pasted.Length == 0)
            {
                return false;
            }

            input = TerminalInputEvent.FromPaste(pasted);
            return true;
        }

        if (fallback.Key != KeyCode.Unknown)
        {
            input = TerminalInputEvent.FromKey(fallback);
            return true;
        }

        if (TryReadBurstPaste(info, out var burstText, out var fallbackKeys))
        {
            if (burstText.Length == 0)
            {
                return false;
            }

            input = TerminalInputEvent.FromPaste(burstText);
            return true;
        }

        if (fallbackKeys.Count > 0)
        {
            foreach (var key in fallbackKeys.Skip(1))
            {
                _pendingKeys.Enqueue(key);
            }

            input = TerminalInputEvent.FromKey(fallbackKeys[0]);
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
        return WaitForInput(PastePayloadWaitAttempts)
            ? Console.ReadKey(intercept: true).KeyChar
            : null;
    }

    private static bool WaitForInput(int attempts = InputWaitAttempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (Console.KeyAvailable)
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }

    private static bool TryReadBurstPaste(
        ConsoleKeyInfo first,
        out string text,
        out List<KeyEvent> fallbackKeys)
    {
        var consumed = new List<ConsoleKeyInfo> { first };
        while (WaitForInput(BurstPasteWaitAttempts))
        {
            var next = Console.ReadKey(intercept: true);
            consumed.Add(next);
            if (!IsPasteBurstCandidate(next))
                break;
        }

        return TryClassifyBurstPaste(consumed, out text, out fallbackKeys);
    }

    internal static bool TryClassifyBurstPaste(
        IReadOnlyList<ConsoleKeyInfo> consumed,
        out string text,
        out List<KeyEvent> fallbackKeys)
    {
        text = "";
        fallbackKeys = [];
        if (consumed.Count == 0)
            return false;

        if (consumed.Any(info => !IsPasteBurstCandidate(info)))
        {
            fallbackKeys = consumed.Select(ConsoleKeyMapper.Map).ToList();
            return false;
        }

        var builder = new StringBuilder();
        var sawLineBreak = false;
        var sawNonWhiteSpace = false;
        foreach (var info in consumed)
        {
            builder.Append(info.KeyChar);
            sawLineBreak |= IsLineBreak(info.KeyChar);
            sawNonWhiteSpace |= !char.IsWhiteSpace(info.KeyChar);
        }

        if ((!sawLineBreak && builder.Length < BurstPasteMinimumLength) ||
            (sawLineBreak && (builder.Length < 2 || !sawNonWhiteSpace)))
        {
            fallbackKeys = consumed.Select(ConsoleKeyMapper.Map).ToList();
            return false;
        }

        text = builder.ToString();
        return true;
    }

    private static bool IsPasteBurstCandidate(ConsoleKeyInfo info)
    {
        if ((info.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Control)) != 0)
        {
            return false;
        }

        return info.KeyChar switch
        {
            '\0' => false,
            '\t' or '\r' or '\n' => true,
            _ => !char.IsControl(info.KeyChar)
        };
    }

    private static bool IsLineBreak(char ch) => ch is '\r' or '\n';

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

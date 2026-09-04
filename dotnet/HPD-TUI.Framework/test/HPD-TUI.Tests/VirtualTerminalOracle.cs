using System.Buffers;
using System.Globalization;
using HPD.TUI.Utilities;

namespace HPD.TUI.Tests;

/// <summary>A strict, deterministic virtual terminal used as the ANSI conformance oracle.</summary>
internal sealed class VirtualTerminalOracle
{
    private Cell[,] _cells;
    private int _top;
    private int _bottom;
    private int _savedX;
    private int _savedY;
    private bool _wrapPending;
    private string _style = "0";

    internal VirtualTerminalOracle(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _cells = new Cell[height, width];
        _bottom = height - 1;
    }

    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal int CursorX { get; private set; }
    internal int CursorY { get; private set; }
    internal bool CursorVisible { get; private set; } = true;
    internal int CursorShape { get; private set; }
    internal bool Autowrap { get; private set; } = true;
    internal int SynchronizedOutputDepth { get; private set; }
    internal string? ActiveHyperlink { get; private set; }
    internal List<string> Scrollback { get; } = [];

    internal Cell this[int x, int y] => _cells[y, x];

    internal string Line(int row)
    {
        var builder = new StringBuilder();
        for (var x = 0; x < Width; x++)
            if (!_cells[row, x].Continuation)
                builder.Append(_cells[row, x].Text ?? " ");
        return builder.ToString().TrimEnd();
    }

    internal void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var replacement = new Cell[height, width];
        for (var y = 0; y < Math.Min(height, Height); y++)
            for (var x = 0; x < Math.Min(width, Width); x++)
                replacement[y, x] = _cells[y, x];
        _cells = replacement;
        Width = width;
        Height = height;
        _top = 0;
        _bottom = height - 1;
        CursorX = Math.Clamp(CursorX, 0, width - 1);
        CursorY = Math.Clamp(CursorY, 0, height - 1);
        _wrapPending = false;
    }

    internal void Apply(ReadOnlySpan<char> input)
    {
        for (var index = 0; index < input.Length;)
        {
            var current = input[index];
            if (current == '\x1b')
            {
                index = ApplyEscape(input, index);
                continue;
            }
            if (current == '\r') { CursorX = 0; _wrapPending = false; index++; continue; }
            if (current == '\n') { LineFeed(); index++; continue; }
            if (current == '\b') { CursorX = Math.Max(0, CursorX - 1); _wrapPending = false; index++; continue; }
            if (current == '\t') { CursorX = Math.Min(Width - 1, (CursorX / 8 + 1) * 8); index++; continue; }
            if (char.IsControl(current))
                throw Unsupported($"control U+{(int)current:X4}");

            var runEnd = index + 1;
            while (runEnd < input.Length && input[runEnd] != '\x1b' && !char.IsControl(input[runEnd])) runEnd++;
            var printable = input[index..runEnd];
            while (!printable.IsEmpty)
            {
                var length = StringInfo.GetNextTextElementLength(printable);
                Write(printable[..length]);
                printable = printable[length..];
            }
            index = runEnd;
        }
    }

    private int ApplyEscape(ReadOnlySpan<char> input, int start)
    {
        if (start + 1 >= input.Length) throw Unsupported("truncated ESC");
        return input[start + 1] switch
        {
            '[' => ApplyCsi(input, start + 2),
            ']' => ApplyOsc(input, start + 2),
            '7' => Save(start + 2),
            '8' => Restore(start + 2),
            'D' => Index(start + 2),
            'M' => ReverseIndex(start + 2),
            _ => throw Unsupported($"ESC {input[start + 1]}")
        };
    }

    private int ApplyCsi(ReadOnlySpan<char> input, int start)
    {
        var end = start;
        while (end < input.Length && input[end] is >= '0' and <= '?' or >= ' ' and <= '/') end++;
        if (end >= input.Length) throw Unsupported("truncated CSI");
        var final = input[end];
        if (final is < '@' or > '~') throw Unsupported("malformed CSI");
        var body = input[start..end];
        var privateMode = body.Length > 0 && body[0] == '?';
        if (privateMode) body = body[1..];
        var intermediate = body.Length > 0 && body[^1] == ' ' ? ' ' : '\0';
        if (intermediate != '\0') body = body[..^1];
        var parameters = ParseParameters(body);
        var first = Param(parameters, 0, 1);

        if (privateMode)
        {
            if (final is not ('h' or 'l') || parameters.Length != 1)
                throw Unsupported("private CSI");
            var enabled = final == 'h';
            switch (parameters[0])
            {
                case 7: Autowrap = enabled; _wrapPending = false; break;
                case 25: CursorVisible = enabled; break;
                case 1049: Clear(); CursorX = CursorY = 0; break;
                case 2026: SynchronizedOutputDepth += enabled ? 1 : -1; if (SynchronizedOutputDepth < 0) throw Unsupported("unbalanced synchronized output"); break;
                default: throw Unsupported($"private mode {parameters[0]}");
            }
            return end + 1;
        }

        switch (final)
        {
            case 'A': CursorY = Math.Max(_top, CursorY - first); break;
            case 'B': CursorY = Math.Min(_bottom, CursorY + first); break;
            case 'C': CursorX = Math.Min(Width - 1, CursorX + first); break;
            case 'D': CursorX = Math.Max(0, CursorX - first); break;
            case 'G': CursorX = Math.Clamp(first - 1, 0, Width - 1); break;
            case 'H': case 'f': CursorY = Math.Clamp(Param(parameters, 0, 1) - 1, 0, Height - 1); CursorX = Math.Clamp(Param(parameters, 1, 1) - 1, 0, Width - 1); break;
            case 'J': EraseDisplay(parameters.Length == 0 ? 0 : parameters[0]); break;
            case 'K': EraseLine(parameters.Length == 0 ? 0 : parameters[0]); break;
            case 'm': ApplySgr(parameters); break;
            case 'r': SetMargins(parameters); break;
            case 's': if (parameters.Length != 0) throw Unsupported("parameterized save"); Save(0); break;
            case 'u': if (parameters.Length != 0) throw Unsupported("parameterized restore"); Restore(0); break;
            case 'q' when intermediate == ' ': CursorShape = parameters.Length == 0 ? 0 : parameters[0]; break;
            default: throw Unsupported($"CSI {final}");
        }
        _wrapPending = false;
        return end + 1;
    }

    private int ApplyOsc(ReadOnlySpan<char> input, int start)
    {
        var end = start;
        while (end < input.Length && input[end] != '\a' && !(input[end] == '\x1b' && end + 1 < input.Length && input[end + 1] == '\\')) end++;
        if (end >= input.Length) throw Unsupported("unterminated OSC");
        var payload = input[start..end];
        var firstSeparator = payload.IndexOf(';');
        if (firstSeparator < 0 || !payload[..firstSeparator].SequenceEqual("8")) throw Unsupported("non-OSC-8 command");
        var remainder = payload[(firstSeparator + 1)..];
        var secondSeparator = remainder.IndexOf(';');
        if (secondSeparator < 0) throw Unsupported("malformed OSC 8");
        ActiveHyperlink = remainder[(secondSeparator + 1)..].ToString() is { Length: > 0 } link ? link : null;
        return input[end] == '\a' ? end + 1 : end + 2;
    }

    private void Write(ReadOnlySpan<char> grapheme)
    {
        var width = 0;
        var runes = new RuneEnumerator(grapheme);
        while (runes.MoveNext()) width = Math.Max(width, UnicodeWidth.GetWidth(runes.Current));
        if (width == 0)
        {
            var x = Math.Max(0, CursorX - 1);
            while (x > 0 && _cells[CursorY, x].Continuation) x--;
            _cells[CursorY, x] = _cells[CursorY, x] with { Text = (_cells[CursorY, x].Text ?? "") + grapheme.ToString() };
            return;
        }
        if (width is not (1 or 2)) throw Unsupported($"rune width {width}");
        if (_wrapPending)
        {
            if (Autowrap) { CursorX = 0; LineFeed(); }
            _wrapPending = false;
        }
        if (width == 2 && CursorX == Width - 1)
        {
            if (!Autowrap) return;
            CursorX = 0;
            LineFeed();
        }
        ClearGlyphAt(CursorX, CursorY);
        _cells[CursorY, CursorX] = new(grapheme.ToString(), false, ActiveHyperlink, _style);
        if (width == 2) _cells[CursorY, CursorX + 1] = new(null, true, ActiveHyperlink, _style);
        CursorX += width;
        if (CursorX >= Width)
        {
            CursorX = Width - 1;
            _wrapPending = Autowrap;
        }
    }

    private void LineFeed()
    {
        _wrapPending = false;
        if (CursorY == _bottom) ScrollUp();
        else CursorY = Math.Min(Height - 1, CursorY + 1);
    }

    private void ScrollUp()
    {
        if (_top == 0) Scrollback.Add(Line(0));
        for (var y = _top; y < _bottom; y++)
            for (var x = 0; x < Width; x++) _cells[y, x] = _cells[y + 1, x];
        ClearRow(_bottom);
    }

    private void ClearGlyphAt(int x, int y)
    {
        if (_cells[y, x].Continuation && x > 0) _cells[y, x - 1] = default;
        if (!_cells[y, x].Continuation && x + 1 < Width && _cells[y, x + 1].Continuation) _cells[y, x + 1] = default;
        _cells[y, x] = default;
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0: for (var y = CursorY; y < Height; y++) for (var x = y == CursorY ? CursorX : 0; x < Width; x++) ClearGlyphAt(x, y); break;
            case 1: for (var y = 0; y <= CursorY; y++) for (var x = 0; x <= (y == CursorY ? CursorX : Width - 1); x++) ClearGlyphAt(x, y); break;
            case 2: Clear(); break;
            case 3: Scrollback.Clear(); break;
            default: throw Unsupported($"erase display {mode}");
        }
    }

    private void EraseLine(int mode)
    {
        var start = mode == 0 ? CursorX : 0;
        var end = mode == 1 ? CursorX : Width - 1;
        if (mode is < 0 or > 2) throw Unsupported($"erase line {mode}");
        for (var x = start; x <= end; x++) ClearGlyphAt(x, CursorY);
    }

    private void SetMargins(int[] parameters)
    {
        var top = Param(parameters, 0, 1) - 1;
        var bottom = Param(parameters, 1, Height) - 1;
        if (top < 0 || bottom >= Height || top >= bottom) throw Unsupported("invalid scrolling margins");
        _top = top; _bottom = bottom; CursorX = CursorY = 0;
    }

    private void ApplySgr(int[] parameters)
    {
        if (parameters.Length == 0) parameters = [0];
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (parameter is 38 or 48)
            {
                if (index + 1 >= parameters.Length) throw Unsupported("truncated extended SGR color");
                var colorMode = parameters[++index];
                var componentCount = colorMode switch { 2 => 3, 5 => 1, _ => throw Unsupported($"SGR color mode {colorMode}") };
                if (index + componentCount >= parameters.Length) throw Unsupported("truncated extended SGR color");
                for (var component = 0; component < componentCount; component++)
                    if (parameters[++index] is < 0 or > 255) throw Unsupported("SGR color component outside byte range");
                continue;
            }
            if (parameter is not (0 or 1 or 2 or 3 or 4 or 7 or 9 or 22 or 23 or 24 or 27 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 39 or 40 or 41 or 42 or 43 or 44 or 45 or 46 or 47 or 49 or 90 or 91 or 92 or 93 or 94 or 95 or 96 or 97 or 100 or 101 or 102 or 103 or 104 or 105 or 106 or 107))
                throw Unsupported($"SGR {parameter}");
        }
        _style = string.Join(';', parameters);
    }

    private void Clear() => Array.Clear(_cells);
    private void ClearRow(int y) { for (var x = 0; x < Width; x++) _cells[y, x] = default; }
    private int Save(int next) { _savedX = CursorX; _savedY = CursorY; return next; }
    private int Restore(int next) { CursorX = _savedX; CursorY = _savedY; _wrapPending = false; return next; }
    private int Index(int next) { LineFeed(); return next; }
    private int ReverseIndex(int next) { CursorY = Math.Max(_top, CursorY - 1); return next; }

    private static int[] ParseParameters(ReadOnlySpan<char> body)
    {
        if (body.IsEmpty) return [];
        var pieces = body.ToString().Split(';');
        var result = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
            if (pieces[i].Length != 0 && !int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
                throw Unsupported("non-numeric CSI parameter");
        return result;
    }

    private static int Param(int[] parameters, int index, int defaultValue)
        => index >= parameters.Length || parameters[index] == 0 ? defaultValue : parameters[index];

    private static InvalidDataException Unsupported(string sequence)
        => new($"Unsupported or ambiguous terminal sequence: {sequence}.");

    internal readonly record struct Cell(string? Text, bool Continuation, string? Hyperlink, string? Style);
}

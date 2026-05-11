using System.Buffers;

namespace HPD.TUI.Utilities;

public ref struct RuneEnumerator
{
    private ReadOnlySpan<char> _text;
    private int _position;

    public RuneEnumerator(ReadOnlySpan<char> text)
    {
        _text = text;
        _position = 0;
        Current = default;
    }

    public Rune Current { get; private set; }

    public bool MoveNext()
    {
        if (_position >= _text.Length)
        {
            return false;
        }

        var status = Rune.DecodeFromUtf16(_text[_position..], out var rune, out var charsConsumed);
        if (status != OperationStatus.Done)
        {
            rune = Rune.ReplacementChar;
            charsConsumed = 1;
        }

        Current = rune;
        _position += charsConsumed;
        return true;
    }

    public readonly RuneEnumerator GetEnumerator() => this;
}

namespace Helium.Algebra;

internal enum MathTokenKind
{
    End,
    Plus,
    Minus,
    Star,
    Caret,
    Slash,
    LParen,
    RParen,
    LBracket,
    RBracket,
    Comma,
    Semicolon,
    Number,
    Variable,
}

internal readonly struct MathToken
{
    public MathTokenKind Kind { get; }
    public int Start { get; }
    public int Length { get; }
    public int VariableIndex { get; }

    public int End => Start + Length;

    public MathToken(MathTokenKind kind, int start, int length, int variableIndex = -1)
    {
        Kind = kind;
        Start = start;
        Length = length;
        VariableIndex = variableIndex;
    }

    public ReadOnlySpan<char> Slice(ReadOnlySpan<char> source) =>
        source.Slice(Start, Length);
}

internal ref struct MathLexer
{
    private readonly ReadOnlySpan<char> _source;
    private int _pos;

    public MathToken Current { get; private set; }

    public MathLexer(ReadOnlySpan<char> source)
    {
        _source = source;
        _pos = 0;
        Current = default;
        Next();
    }

    public void Next()
    {
        SkipWhitespace();

        if (_pos >= _source.Length)
        {
            Current = new(MathTokenKind.End, _pos, 0);
            return;
        }

        char c = _source[_pos];
        switch (c)
        {
            case '+':
                Current = new(MathTokenKind.Plus, _pos, 1);
                _pos++;
                return;
            case '-':
                Current = new(MathTokenKind.Minus, _pos, 1);
                _pos++;
                return;
            case '*':
                Current = new(MathTokenKind.Star, _pos, 1);
                _pos++;
                return;
            case '^':
                Current = new(MathTokenKind.Caret, _pos, 1);
                _pos++;
                return;
            case '/':
                Current = new(MathTokenKind.Slash, _pos, 1);
                _pos++;
                return;
            case '(':
                Current = new(MathTokenKind.LParen, _pos, 1);
                _pos++;
                return;
            case ')':
                Current = new(MathTokenKind.RParen, _pos, 1);
                _pos++;
                return;
            case '[':
                Current = new(MathTokenKind.LBracket, _pos, 1);
                _pos++;
                return;
            case ']':
                Current = new(MathTokenKind.RBracket, _pos, 1);
                _pos++;
                return;
            case ',':
                Current = new(MathTokenKind.Comma, _pos, 1);
                _pos++;
                return;
            case ';':
                Current = new(MathTokenKind.Semicolon, _pos, 1);
                _pos++;
                return;
        }

        if (char.IsAsciiDigit(c))
        {
            int start = _pos;
            while (_pos < _source.Length && char.IsAsciiDigit(_source[_pos]))
                _pos++;
            Current = new(MathTokenKind.Number, start, _pos - start);
            return;
        }

        if (c is 'x' or 'X')
        {
            int start = _pos;
            _pos++; // x
            int indexStart = _pos;
            while (_pos < _source.Length && char.IsAsciiDigit(_source[_pos]))
                _pos++;

            int varIndex = 0;
            if (_pos > indexStart)
            {
                if (!int.TryParse(_source[indexStart.._pos], out varIndex))
                    throw new FormatException("Invalid variable index.");
            }

            Current = new(MathTokenKind.Variable, start, _pos - start, varIndex);
            return;
        }

        if (c is 'y' or 'Y')
        {
            Current = new(MathTokenKind.Variable, _pos, 1, 1);
            _pos++;
            return;
        }

        if (c is 'z' or 'Z')
        {
            Current = new(MathTokenKind.Variable, _pos, 1, 2);
            _pos++;
            return;
        }

        throw new FormatException($"Unexpected character '{c}'.");
    }

    private void SkipWhitespace()
    {
        while (_pos < _source.Length && char.IsWhiteSpace(_source[_pos]))
            _pos++;
    }
}

internal static class SpanTrim
{
    internal static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
    {
        int start = 0;
        while (start < s.Length && char.IsWhiteSpace(s[start]))
            start++;

        int end = s.Length - 1;
        while (end >= start && char.IsWhiteSpace(s[end]))
            end--;

        return s[start..(end + 1)];
    }
}


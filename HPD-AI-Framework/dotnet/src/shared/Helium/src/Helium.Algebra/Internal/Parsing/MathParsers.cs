using Helium.Primitives;

namespace Helium.Algebra;

internal static class UnivariatePolynomialParser
{
    internal static Polynomial<R> Parse<R>(ReadOnlySpan<char> s, IFormatProvider? provider)
        where R : IRing<R>, ISpanParsable<R>
    {
        var parser = new Parser<R>(s, provider);
        var result = parser.ParsePolynomial();
        parser.Expect(MathTokenKind.End);
        return result;
    }

    internal static bool TryParse<R>(ReadOnlySpan<char> s, IFormatProvider? provider, out Polynomial<R> result)
        where R : IRing<R>, ISpanParsable<R>
    {
        try
        {
            result = Parse<R>(s, provider);
            return true;
        }
        catch
        {
            result = Polynomial<R>.Zero;
            return false;
        }
    }

    private ref struct Parser<R>
        where R : IRing<R>, ISpanParsable<R>
    {
        private readonly ReadOnlySpan<char> _source;
        private readonly IFormatProvider? _provider;
        private MathLexer _lexer;

        public Parser(ReadOnlySpan<char> source, IFormatProvider? provider)
        {
            _source = source;
            _provider = provider;
            _lexer = new MathLexer(source);
        }

        public Polynomial<R> ParsePolynomial()
        {
            if (_lexer.Current.Kind is MathTokenKind.End)
                throw new FormatException("Empty polynomial.");

            var result = ParseSignedTerm();
            while (_lexer.Current.Kind is MathTokenKind.Plus or MathTokenKind.Minus)
            {
                var op = _lexer.Current.Kind;
                _lexer.Next();
                var term = ParseTerm();
                result = op == MathTokenKind.Plus ? result + term : result - term;
            }
            return result;
        }

        private Polynomial<R> ParseSignedTerm()
        {
            bool negative = false;
            if (_lexer.Current.Kind == MathTokenKind.Plus)
            {
                _lexer.Next();
            }
            else if (_lexer.Current.Kind == MathTokenKind.Minus)
            {
                negative = true;
                _lexer.Next();
            }

            var term = ParseTerm();
            return negative ? -term : term;
        }

        private Polynomial<R> ParseTerm()
        {
            var result = ParseFactor();

            while (true)
            {
                if (_lexer.Current.Kind == MathTokenKind.Star)
                {
                    _lexer.Next();
                    result = result * ParseFactor();
                    continue;
                }

                if (CanStartFactor(_lexer.Current.Kind))
                {
                    // Implicit multiplication: "3x" or "(2/3)x^2".
                    result = result * ParseFactor();
                    continue;
                }

                break;
            }

            return result;
        }

        private static bool CanStartFactor(MathTokenKind kind) =>
            kind is MathTokenKind.Number or MathTokenKind.Variable or MathTokenKind.LParen;

        private Polynomial<R> ParseFactor()
        {
            return _lexer.Current.Kind switch
            {
                MathTokenKind.Number or MathTokenKind.LParen => Polynomial<R>.C(ParseScalar()),
                MathTokenKind.Variable => ParseVariableFactor(),
                _ => throw new FormatException("Expected term factor.")
            };
        }

        private Polynomial<R> ParseVariableFactor()
        {
            // We treat x/y/z/xN all as the single indeterminate for Polynomial<R>.
            _lexer.Next();

            int exp = 1;
            if (_lexer.Current.Kind == MathTokenKind.Caret)
            {
                _lexer.Next();
                exp = ParseNonNegativeInt();
            }

            return Polynomial<R>.Monomial(exp, R.MultiplicativeIdentity);
        }

        private R ParseScalar()
        {
            if (_lexer.Current.Kind == MathTokenKind.LParen)
            {
                _lexer.Next();
                var value = ParseScalarLiteral();
                Expect(MathTokenKind.RParen);
                _lexer.Next();
                return value;
            }

            return ParseScalarLiteral();
        }

        private R ParseScalarLiteral()
        {
            bool negative = false;
            if (_lexer.Current.Kind == MathTokenKind.Plus)
            {
                _lexer.Next();
            }
            else if (_lexer.Current.Kind == MathTokenKind.Minus)
            {
                negative = true;
                _lexer.Next();
            }

            var span = ParseUnsignedNumericLiteral();
            var value = R.Parse(span, _provider);
            return negative ? -value : value;
        }

        private ReadOnlySpan<char> ParseUnsignedNumericLiteral()
        {
            if (_lexer.Current.Kind != MathTokenKind.Number)
                throw new FormatException("Expected number literal.");

            int start = _lexer.Current.Start;
            int end = _lexer.Current.End;
            _lexer.Next();

            if (_lexer.Current.Kind == MathTokenKind.Slash)
            {
                _lexer.Next();
                if (_lexer.Current.Kind != MathTokenKind.Number)
                    throw new FormatException("Expected denominator after '/'.");
                end = _lexer.Current.End;
                _lexer.Next();
            }

            return _source[start..end];
        }

        private int ParseNonNegativeInt()
        {
            bool negative = false;
            if (_lexer.Current.Kind == MathTokenKind.Plus)
            {
                _lexer.Next();
            }
            else if (_lexer.Current.Kind == MathTokenKind.Minus)
            {
                negative = true;
                _lexer.Next();
            }

            if (_lexer.Current.Kind != MathTokenKind.Number)
                throw new FormatException("Expected integer exponent.");

            var span = _lexer.Current.Slice(_source);
            if (!int.TryParse(span, out var value))
                throw new FormatException("Invalid integer exponent.");
            _lexer.Next();

            if (negative)
                value = -value;

            if (value < 0)
                throw new FormatException("Negative exponents are not allowed for polynomials.");

            return value;
        }

        public void Expect(MathTokenKind kind)
        {
            if (_lexer.Current.Kind != kind)
                throw new FormatException($"Expected {kind}.");
        }
    }
}

internal static class MvPolynomialParser
{
    internal static MvPolynomial<R> Parse<R>(ReadOnlySpan<char> s, IFormatProvider? provider)
        where R : ICommRing<R>, ISpanParsable<R>
    {
        var parser = new Parser<R>(s, provider);
        var result = parser.ParsePolynomial();
        parser.Expect(MathTokenKind.End);
        return result;
    }

    internal static bool TryParse<R>(ReadOnlySpan<char> s, IFormatProvider? provider, out MvPolynomial<R> result)
        where R : ICommRing<R>, ISpanParsable<R>
    {
        try
        {
            result = Parse<R>(s, provider);
            return true;
        }
        catch
        {
            result = MvPolynomial<R>.Zero;
            return false;
        }
    }

    private ref struct Parser<R>
        where R : ICommRing<R>, ISpanParsable<R>
    {
        private readonly ReadOnlySpan<char> _source;
        private readonly IFormatProvider? _provider;
        private MathLexer _lexer;

        public Parser(ReadOnlySpan<char> source, IFormatProvider? provider)
        {
            _source = source;
            _provider = provider;
            _lexer = new MathLexer(source);
        }

        public MvPolynomial<R> ParsePolynomial()
        {
            if (_lexer.Current.Kind is MathTokenKind.End)
                throw new FormatException("Empty polynomial.");

            var result = ParseSignedTerm();
            while (_lexer.Current.Kind is MathTokenKind.Plus or MathTokenKind.Minus)
            {
                var op = _lexer.Current.Kind;
                _lexer.Next();
                var term = ParseTerm();
                result = op == MathTokenKind.Plus ? result + term : result - term;
            }
            return result;
        }

        private MvPolynomial<R> ParseSignedTerm()
        {
            bool negative = false;
            if (_lexer.Current.Kind == MathTokenKind.Plus)
            {
                _lexer.Next();
            }
            else if (_lexer.Current.Kind == MathTokenKind.Minus)
            {
                negative = true;
                _lexer.Next();
            }

            var term = ParseTerm();
            return negative ? -term : term;
        }

        private MvPolynomial<R> ParseTerm()
        {
            var coefficient = R.MultiplicativeIdentity;
            var exponents = new Dictionary<int, int>();
            bool sawFactor = false;

            while (true)
            {
                if (_lexer.Current.Kind == MathTokenKind.Star)
                {
                    _lexer.Next();
                    continue;
                }

                if (_lexer.Current.Kind is MathTokenKind.Number or MathTokenKind.LParen)
                {
                    coefficient = coefficient * ParseScalar();
                    sawFactor = true;
                    continue;
                }

                if (_lexer.Current.Kind == MathTokenKind.Variable)
                {
                    int index = _lexer.Current.VariableIndex;
                    _lexer.Next();

                    int exp = 1;
                    if (_lexer.Current.Kind == MathTokenKind.Caret)
                    {
                        _lexer.Next();
                        exp = ParseNonNegativeInt();
                    }

                    exponents[index] = exponents.TryGetValue(index, out var current)
                        ? current + exp
                        : exp;

                    sawFactor = true;
                    continue;
                }

                break;
            }

            if (!sawFactor)
                throw new FormatException("Expected term.");

            var monomial = exponents.Count == 0
                ? Monomial.One
                : Monomial.FromExponents(exponents.Select(kv => new KeyValuePair<int, Integer>(kv.Key, (Integer)kv.Value)));

            return MvPolynomial<R>.Term(monomial, coefficient);
        }

        private R ParseScalar()
        {
            if (_lexer.Current.Kind == MathTokenKind.LParen)
            {
                _lexer.Next();
                var value = ParseScalarLiteral();
                Expect(MathTokenKind.RParen);
                _lexer.Next();
                return value;
            }

            return ParseScalarLiteral();
        }

        private R ParseScalarLiteral()
        {
            bool negative = false;
            if (_lexer.Current.Kind == MathTokenKind.Plus)
            {
                _lexer.Next();
            }
            else if (_lexer.Current.Kind == MathTokenKind.Minus)
            {
                negative = true;
                _lexer.Next();
            }

            var span = ParseUnsignedNumericLiteral();
            var value = R.Parse(span, _provider);
            return negative ? -value : value;
        }

        private ReadOnlySpan<char> ParseUnsignedNumericLiteral()
        {
            if (_lexer.Current.Kind != MathTokenKind.Number)
                throw new FormatException("Expected number literal.");

            int start = _lexer.Current.Start;
            int end = _lexer.Current.End;
            _lexer.Next();

            if (_lexer.Current.Kind == MathTokenKind.Slash)
            {
                _lexer.Next();
                if (_lexer.Current.Kind != MathTokenKind.Number)
                    throw new FormatException("Expected denominator after '/'.");
                end = _lexer.Current.End;
                _lexer.Next();
            }

            return _source[start..end];
        }

        private int ParseNonNegativeInt()
        {
            bool negative = false;
            if (_lexer.Current.Kind == MathTokenKind.Plus)
            {
                _lexer.Next();
            }
            else if (_lexer.Current.Kind == MathTokenKind.Minus)
            {
                negative = true;
                _lexer.Next();
            }

            if (_lexer.Current.Kind != MathTokenKind.Number)
                throw new FormatException("Expected integer exponent.");

            var span = _lexer.Current.Slice(_source);
            if (!int.TryParse(span, out var value))
                throw new FormatException("Invalid integer exponent.");
            _lexer.Next();

            if (negative)
                value = -value;

            if (value < 0)
                throw new FormatException("Negative exponents are not allowed for polynomials.");

            return value;
        }

        public void Expect(MathTokenKind kind)
        {
            if (_lexer.Current.Kind != kind)
                throw new FormatException($"Expected {kind}.");
        }
    }
}

internal static class MatrixParser
{
    internal static Matrix<R> Parse<R>(ReadOnlySpan<char> s, IFormatProvider? provider)
        where R : IRing<R>, ISpanParsable<R>
    {
        var parser = new Parser<R>(s, provider);
        var result = parser.ParseMatrix();
        parser.Expect(MathTokenKind.End);
        return result;
    }

    internal static bool TryParse<R>(ReadOnlySpan<char> s, IFormatProvider? provider, out Matrix<R> result)
        where R : IRing<R>, ISpanParsable<R>
    {
        try
        {
            result = Parse<R>(s, provider);
            return true;
        }
        catch
        {
            result = Matrix<R>.Zero(0, 0);
            return false;
        }
    }

    private ref struct Parser<R>
        where R : IRing<R>, ISpanParsable<R>
    {
        private readonly ReadOnlySpan<char> _source;
        private readonly IFormatProvider? _provider;
        private MathLexer _lexer;

        public Parser(ReadOnlySpan<char> source, IFormatProvider? provider)
        {
            _source = source;
            _provider = provider;
            _lexer = new MathLexer(source);
        }

        public Matrix<R> ParseMatrix()
        {
            Expect(MathTokenKind.LBracket);
            _lexer.Next();

            var rows = _lexer.Current.Kind == MathTokenKind.LBracket
                ? ParseBracketRows()
                : ParseMatlabRows();

            Expect(MathTokenKind.RBracket);
            _lexer.Next();

            if (rows.Count == 0)
                return Matrix<R>.Zero(0, 0);

            int cols = rows[0].Count;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Count != cols)
                    throw new FormatException("Ragged matrix rows are not allowed.");

            var data = new R[rows.Count * cols];
            int k = 0;
            foreach (var row in rows)
                foreach (var value in row)
                    data[k++] = value;

            return Matrix<R>.FromArray(rows.Count, cols, data);
        }

        private List<List<R>> ParseBracketRows()
        {
            var rows = new List<List<R>>();
            while (true)
            {
                Expect(MathTokenKind.LBracket);
                _lexer.Next();

                rows.Add(ParseRowUntil(MathTokenKind.RBracket));

                Expect(MathTokenKind.RBracket);
                _lexer.Next();

                if (_lexer.Current.Kind == MathTokenKind.Comma)
                {
                    _lexer.Next();
                    continue;
                }

                break;
            }
            return rows;
        }

        private List<List<R>> ParseMatlabRows()
        {
            var rows = new List<List<R>>
            {
                ParseRowUntil(MathTokenKind.Semicolon, MathTokenKind.RBracket)
            };

            while (_lexer.Current.Kind == MathTokenKind.Semicolon)
            {
                _lexer.Next();
                rows.Add(ParseRowUntil(MathTokenKind.Semicolon, MathTokenKind.RBracket));
            }

            return rows;
        }

        private List<R> ParseRowUntil(params MathTokenKind[] terminators)
        {
            var row = new List<R>();

            if (terminators.Contains(_lexer.Current.Kind))
                return row; // empty row

            row.Add(ParseScalarLiteral());
            while (_lexer.Current.Kind == MathTokenKind.Comma)
            {
                _lexer.Next();
                row.Add(ParseScalarLiteral());
            }

            return row;
        }

        private R ParseScalarLiteral()
        {
            bool negative = false;
            if (_lexer.Current.Kind == MathTokenKind.Plus)
            {
                _lexer.Next();
            }
            else if (_lexer.Current.Kind == MathTokenKind.Minus)
            {
                negative = true;
                _lexer.Next();
            }

            var span = ParseUnsignedNumericLiteral();
            var value = R.Parse(span, _provider);
            return negative ? -value : value;
        }

        private ReadOnlySpan<char> ParseUnsignedNumericLiteral()
        {
            if (_lexer.Current.Kind != MathTokenKind.Number)
                throw new FormatException("Expected number literal.");

            int start = _lexer.Current.Start;
            int end = _lexer.Current.End;
            _lexer.Next();

            if (_lexer.Current.Kind == MathTokenKind.Slash)
            {
                _lexer.Next();
                if (_lexer.Current.Kind != MathTokenKind.Number)
                    throw new FormatException("Expected denominator after '/'.");
                end = _lexer.Current.End;
                _lexer.Next();
            }

            return _source[start..end];
        }

        public void Expect(MathTokenKind kind)
        {
            if (_lexer.Current.Kind != kind)
                throw new FormatException($"Expected {kind}.");
        }
    }
}


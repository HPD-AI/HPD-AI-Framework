using System.Text;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Dense matrix over a ring R. Flat row-major array.
/// </summary>
public readonly struct Matrix<R> : IEquatable<Matrix<R>>, IFormattable
    where R : IRing<R>
{
    private readonly R[] _data;
    public int Rows { get; }
    public int Cols { get; }

    private Matrix(int rows, int cols, R[] data)
    {
        Rows = rows;
        Cols = cols;
        _data = data;
    }

    // --- Construction ---

    public static Matrix<R> Zero(int rows, int cols)
    {
        var data = new R[rows * cols];
        Array.Fill(data, R.AdditiveIdentity);
        return new(rows, cols, data);
    }

    public static Matrix<R> Identity(int n)
    {
        var data = new R[n * n];
        Array.Fill(data, R.AdditiveIdentity);
        for (int i = 0; i < n; i++)
            data[i * n + i] = R.MultiplicativeIdentity;
        return new(n, n, data);
    }

    public static Matrix<R> FromArray(int rows, int cols, ReadOnlySpan<R> values) =>
        new(rows, cols, values.ToArray());

    public static Matrix<R> FromRows(R[][] rows)
    {
        int r = rows.Length;
        int c = rows[0].Length;
        var data = new R[r * c];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
                data[i * c + j] = rows[i][j];
        return new(r, c, data);
    }

    // --- Access ---

    public R this[int i, int j] => _data[i * Cols + j];

    public ReadOnlySpan<R> Row(int i) => _data.AsSpan(i * Cols, Cols);

    internal Span<R> RowMut(int i) => _data.AsSpan(i * Cols, Cols);

    internal ReadOnlySpan<R> AsSpan() => _data;

    // --- Arithmetic ---

    public static Matrix<R> operator +(Matrix<R> left, Matrix<R> right)
    {
        var data = new R[left.Rows * left.Cols];
        for (int i = 0; i < data.Length; i++)
            data[i] = left._data[i] + right._data[i];
        return new(left.Rows, left.Cols, data);
    }

    public static Matrix<R> operator -(Matrix<R> left, Matrix<R> right)
    {
        var data = new R[left.Rows * left.Cols];
        for (int i = 0; i < data.Length; i++)
            data[i] = left._data[i] - right._data[i];
        return new(left.Rows, left.Cols, data);
    }

    public static Matrix<R> operator -(Matrix<R> m)
    {
        var data = new R[m.Rows * m.Cols];
        for (int i = 0; i < data.Length; i++)
            data[i] = -m._data[i];
        return new(m.Rows, m.Cols, data);
    }

    public static Matrix<R> operator *(R scalar, Matrix<R> m)
    {
        var data = new R[m.Rows * m.Cols];
        for (int i = 0; i < data.Length; i++)
            data[i] = scalar * m._data[i];
        return new(m.Rows, m.Cols, data);
    }

    /// <summary>
    /// Matrix multiplication: (M * N)[i,k] = sum_j M[i,j] * N[j,k].
    /// </summary>
    public static Matrix<R> operator *(Matrix<R> left, Matrix<R> right)
    {
        var data = new R[left.Rows * right.Cols];
        for (int i = 0; i < left.Rows; i++)
        {
            for (int k = 0; k < right.Cols; k++)
            {
                var sum = R.AdditiveIdentity;
                for (int j = 0; j < left.Cols; j++)
                    sum = sum + left[i, j] * right[j, k];
                data[i * right.Cols + k] = sum;
            }
        }
        return new(left.Rows, right.Cols, data);
    }

    /// <summary>Matrix-vector product.</summary>
    public static Vector<R> operator *(Matrix<R> m, Vector<R> v)
    {
        var data = new R[m.Rows];
        for (int i = 0; i < m.Rows; i++)
        {
            var sum = R.AdditiveIdentity;
            for (int j = 0; j < m.Cols; j++)
                sum = sum + m[i, j] * v[j];
            data[i] = sum;
        }
        return Vector<R>.FromArray(data);
    }

    // --- Transpose ---

    public Matrix<R> Transpose()
    {
        var data = new R[Rows * Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                data[j * Rows + i] = this[i, j];
        return new(Cols, Rows, data);
    }

    // --- Equality ---

    public bool Equals(Matrix<R> other)
    {
        if (Rows != other.Rows || Cols != other.Cols) return false;
        for (int i = 0; i < _data.Length; i++)
            if (!_data[i].Equals(other._data[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is Matrix<R> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rows);
        hash.Add(Cols);
        foreach (var v in _data) hash.Add(v);
        return hash.ToHashCode();
    }

    public static bool operator ==(Matrix<R> left, Matrix<R> right) => left.Equals(right);
    public static bool operator !=(Matrix<R> left, Matrix<R> right) => !left.Equals(right);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        return format switch
        {
            "L" => FormatLatex(provider),
            "M" => FormatMathML(provider),
            _ => FormatDefault(format, provider)
        };
    }

    private string FormatDefault(string? format, IFormatProvider? provider)
    {
        var rows = new string[Rows];
        for (int i = 0; i < Rows; i++)
        {
            var cols = new string[Cols];
            for (int j = 0; j < Cols; j++)
                cols[j] = FormatHelpers.FormatElement(this[i, j], format, provider);
            rows[i] = "[" + string.Join(", ", cols) + "]";
        }
        return "[" + string.Join(", ", rows) + "]";
    }

    private string FormatLatex(IFormatProvider? provider)
    {
        var sb = new StringBuilder(@"\begin{pmatrix} ");
        for (int i = 0; i < Rows; i++)
        {
            if (i > 0) sb.Append(@" \\ ");
            for (int j = 0; j < Cols; j++)
            {
                if (j > 0) sb.Append(" & ");
                sb.Append(FormatHelpers.FormatElement(this[i, j], "L", provider));
            }
        }
        sb.Append(@" \end{pmatrix}");
        return sb.ToString();
    }

    private string FormatMathML(IFormatProvider? provider)
    {
        var sb = new StringBuilder("<mrow><mo>(</mo><mtable>");
        for (int i = 0; i < Rows; i++)
        {
            sb.Append("<mtr>");
            for (int j = 0; j < Cols; j++)
            {
                sb.Append("<mtd>");
                sb.Append(FormatHelpers.FormatElement(this[i, j], "M", provider));
                sb.Append("</mtd>");
            }
            sb.Append("</mtr>");
        }
        sb.Append("</mtable><mo>)</mo></mrow>");
        return sb.ToString();
    }
}

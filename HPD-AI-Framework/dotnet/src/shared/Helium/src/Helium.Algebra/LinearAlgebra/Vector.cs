using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Dense vector over a ring R. Flat array backed.
/// </summary>
public readonly struct Vector<R> : IEquatable<Vector<R>>, IFormattable
    where R : IRing<R>
{
    private readonly R[] _data;

    public int Length { get; }

    private Vector(R[] data)
    {
        _data = data;
        Length = data.Length;
    }

    // --- Construction ---

    public static Vector<R> Zero(int length)
    {
        var data = new R[length];
        Array.Fill(data, R.AdditiveIdentity);
        return new(data);
    }

    public static Vector<R> FromArray(params ReadOnlySpan<R> values) => new(values.ToArray());

    public static Vector<R> StandardBasis(int length, int index)
    {
        var data = new R[length];
        Array.Fill(data, R.AdditiveIdentity);
        data[index] = R.MultiplicativeIdentity;
        return new(data);
    }

    // --- Access ---

    public R this[int i] => _data[i];

    // --- Arithmetic ---

    public static Vector<R> operator +(Vector<R> left, Vector<R> right)
    {
        var data = new R[left.Length];
        for (int i = 0; i < left.Length; i++)
            data[i] = left._data[i] + right._data[i];
        return new(data);
    }

    public static Vector<R> operator -(Vector<R> left, Vector<R> right)
    {
        var data = new R[left.Length];
        for (int i = 0; i < left.Length; i++)
            data[i] = left._data[i] - right._data[i];
        return new(data);
    }

    public static Vector<R> operator -(Vector<R> v)
    {
        var data = new R[v.Length];
        for (int i = 0; i < v.Length; i++)
            data[i] = -v._data[i];
        return new(data);
    }

    public static Vector<R> operator *(R scalar, Vector<R> v)
    {
        var data = new R[v.Length];
        for (int i = 0; i < v.Length; i++)
            data[i] = scalar * v._data[i];
        return new(data);
    }

    /// <summary>
    /// Dot product (requires commutative ring for the standard interpretation).
    /// </summary>
    public static R Dot(Vector<R> left, Vector<R> right)
    {
        var sum = R.AdditiveIdentity;
        for (int i = 0; i < left.Length; i++)
            sum = sum + left._data[i] * right._data[i];
        return sum;
    }

    // --- Equality ---

    public bool Equals(Vector<R> other)
    {
        if (Length != other.Length) return false;
        for (int i = 0; i < Length; i++)
            if (!_data[i].Equals(other._data[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is Vector<R> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var v in _data) hash.Add(v);
        return hash.ToHashCode();
    }

    public static bool operator ==(Vector<R> left, Vector<R> right) => left.Equals(right);
    public static bool operator !=(Vector<R> left, Vector<R> right) => !left.Equals(right);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        return format switch
        {
            "L" => @"\begin{pmatrix} " + string.Join(@" \\ ", _data.Select(x => FormatHelpers.FormatElement(x, "L", provider))) + @" \end{pmatrix}",
            "M" => "<mrow><mo>(</mo><mtable>" + string.Concat(_data.Select(x => $"<mtr><mtd>{FormatHelpers.FormatElement(x, "M", provider)}</mtd></mtr>")) + "</mtable><mo>)</mo></mrow>",
            _ => "[" + string.Join(", ", _data.Select(x => FormatHelpers.FormatElement(x, format, provider))) + "]"
        };
    }
}

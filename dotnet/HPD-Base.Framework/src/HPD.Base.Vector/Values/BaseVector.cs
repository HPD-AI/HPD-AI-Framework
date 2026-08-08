using System.Globalization;

namespace HPD.Base;

/// <summary>Contains one immutable, finite, application-supplied float32 vector.</summary>
public readonly struct BaseVector : IEquatable<BaseVector>
{
    private readonly float[]? _values;

    private BaseVector(ReadOnlySpan<float> values) => _values = values.ToArray();

    /// <summary>Gets the number of vector dimensions, or zero for an invalid default value.</summary>
    public int Dimensions => _values?.Length ?? 0;

    /// <summary>Gets the value at the requested dimension.</summary>
    /// <param name="index">The zero-based dimension.</param>
    /// <returns>The dimension value.</returns>
    public float this[int index] => (_values ?? throw InvalidDefault())[index];

    /// <summary>Creates an immutable vector by copying the supplied finite values.</summary>
    /// <param name="values">The non-empty finite vector values.</param>
    /// <returns>An owned immutable vector.</returns>
    /// <exception cref="ArgumentException">The values are empty or contain a non-finite number.</exception>
    public static BaseVector Create(ReadOnlySpan<float> values)
    {
        if (!TryCreate(values, out BaseVector vector))
            throw new ArgumentException("A vector must contain one or more finite values.", nameof(values));
        return vector;
    }

    /// <summary>Attempts to create an immutable vector by copying the supplied values.</summary>
    /// <param name="values">The candidate vector values.</param>
    /// <param name="vector">The created vector when validation succeeds.</param>
    /// <returns><see langword="true"/> when every value is finite and at least one dimension exists.</returns>
    public static bool TryCreate(ReadOnlySpan<float> values, out BaseVector vector)
    {
        if (values.IsEmpty)
        {
            vector = default;
            return false;
        }
        foreach (float value in values)
        {
            if (!float.IsFinite(value))
            {
                vector = default;
                return false;
            }
        }
        vector = new BaseVector(values);
        return true;
    }

    /// <summary>Copies the vector into caller-owned storage.</summary>
    /// <param name="destination">The destination with at least <see cref="Dimensions"/> elements.</param>
    public void CopyTo(Span<float> destination) => (_values ?? throw InvalidDefault()).CopyTo(destination);

    /// <summary>Returns a new caller-owned copy of the vector values.</summary>
    /// <returns>A new mutable array that does not alias this value.</returns>
    public float[] ToArray() => [.. _values ?? throw InvalidDefault()];

    /// <inheritdoc />
    public bool Equals(BaseVector other)
    {
        if (_values is null || other._values is null)
            return _values is null && other._values is null;
        if (_values.Length != other._values.Length)
            return false;
        for (var index = 0; index < _values.Length; index++)
        {
            if (BitConverter.SingleToInt32Bits(_values[index]) != BitConverter.SingleToInt32Bits(other._values[index]))
                return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseVector other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (_values is not null)
        {
            foreach (float value in _values)
                hash.Add(BitConverter.SingleToInt32Bits(value));
        }
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => _values is null
        ? "BaseVector(invalid)"
        : string.Create(CultureInfo.InvariantCulture, $"BaseVector(dimensions={_values.Length})");

    /// <summary>Determines whether two vectors contain the same float32 bit sequence.</summary>
    public static bool operator ==(BaseVector left, BaseVector right) => left.Equals(right);

    /// <summary>Determines whether two vectors contain different float32 bit sequences.</summary>
    public static bool operator !=(BaseVector left, BaseVector right) => !left.Equals(right);

    internal bool HasNonZeroNorm()
    {
        if (_values is null)
            return false;
        double squaredNorm = 0;
        foreach (float value in _values)
            squaredNorm += (double)value * value;
        return squaredNorm > 0 && double.IsFinite(squaredNorm);
    }

    private static InvalidOperationException InvalidDefault() =>
        new("The default BaseVector value is invalid.");
}

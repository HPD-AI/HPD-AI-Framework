namespace HPD.Math.LinearAlgebra;

/// <summary>
/// Non-owning dense vector view.
/// </summary>
public readonly ref struct VectorView<T>
{
    public VectorView(ReadOnlySpan<T> values)
    {
        Values = values;
    }

    public ReadOnlySpan<T> Values { get; }

    public int Length => Values.Length;

    public T this[int index] => Values[index];
}

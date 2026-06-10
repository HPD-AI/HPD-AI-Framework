namespace HPD.Math.Numerics;

/// <summary>
/// Non-owning view of a truncated p-typical Witt vector.
/// </summary>
public readonly ref struct WittVectorView<T>
{
    public WittVectorView(ReadOnlySpan<T> components)
    {
        Components = components;
    }

    public ReadOnlySpan<T> Components { get; }

    public int Length => Components.Length;

    public T this[int index] => Components[index];
}

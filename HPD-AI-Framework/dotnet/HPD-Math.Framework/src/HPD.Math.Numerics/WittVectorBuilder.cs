namespace HPD.Math.Numerics;

/// <summary>
/// Caller-owned destination for truncated p-typical Witt vector components.
/// </summary>
public ref struct WittVectorBuilder<T>
{
    private readonly Span<T> _components;
    private int _written;

    public WittVectorBuilder(Span<T> components)
    {
        _components = components;
        _written = 0;
    }

    public int Capacity => _components.Length;

    public int Written => _written;

    public Span<T> WrittenSpan => _components[.._written];

    public void Clear() => _written = 0;

    public void Commit(int length) => _written = length;

    public ref T ComponentAt(int index) => ref _components[index];

    public WittVectorView<T> AsView() => new(_components[.._written]);
}

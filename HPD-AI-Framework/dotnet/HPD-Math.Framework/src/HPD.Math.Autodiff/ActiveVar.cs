namespace HPD.Math.Autodiff;

/// <summary>
/// Active reverse-mode value with its tape node index.
/// </summary>
public readonly struct ActiveVar<T>
{
    public ActiveVar(T value, int index)
    {
        Value = value;
        Index = index;
    }

    public T Value { get; }

    public int Index { get; }
}

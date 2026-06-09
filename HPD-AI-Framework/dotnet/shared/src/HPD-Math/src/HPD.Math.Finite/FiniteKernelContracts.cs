namespace HPD.Math.Finite;

/// <summary>
/// Allocation-free unary mapping operation.
/// </summary>
public interface IMapOps<TInput, TOutput>
{
    void Map(ref TOutput destination, in TInput input);
}

/// <summary>
/// Allocation-free fold over list elements.
/// </summary>
public interface IListFoldOps<TElement, TAccumulator>
{
    void Step(ref TAccumulator accumulator, in TElement element);
}

/// <summary>
/// Allocation-free fold over finite-support entries.
/// </summary>
public interface IFinsuppFoldOps<TKey, TValue, TAccumulator>
{
    void Step(ref TAccumulator accumulator, in TKey key, in TValue value);
}

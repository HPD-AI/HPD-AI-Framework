namespace Helium.Primitives;

/// <summary>
/// A module over a commutative ring.
/// </summary>
public interface IModule<R, M> : IAddCommGroup<M>
    where R : ICommRing<R>
    where M : IModule<R, M>
{
    static abstract M ScalarMultiply(R scalar, M element);
}

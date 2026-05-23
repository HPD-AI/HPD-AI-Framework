using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// A coalgebra over a commutative ring.
/// </summary>
public interface ICoalgebra<R, C> : IModule<R, C>
    where R : ICommRing<R>
    where C : ICoalgebra<R, C>, ITotalOrder<C>
{
    static abstract TensorProduct<R, C, C> Comultiplication(C value);
    static abstract R Counit(C value);
}

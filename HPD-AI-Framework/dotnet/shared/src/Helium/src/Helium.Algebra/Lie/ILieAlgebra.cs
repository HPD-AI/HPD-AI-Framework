using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// A Lie algebra over a commutative ring.
/// </summary>
public interface ILieAlgebra<R, L> :
    ILieRing<L>,
    IModule<R, L>
    where R : ICommRing<R>
    where L : ILieAlgebra<R, L>
{
}

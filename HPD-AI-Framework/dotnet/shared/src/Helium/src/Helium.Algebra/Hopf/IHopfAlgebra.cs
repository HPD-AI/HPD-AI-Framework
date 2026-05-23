using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// A Hopf algebra over a commutative ring.
/// </summary>
public interface IHopfAlgebra<R, A> : ICoalgebra<R, A>
    where R : ICommRing<R>
    where A : IHopfAlgebra<R, A>, ITotalOrder<A>
{
    static abstract A Multiply(A left, A right);
    static abstract A Unit(R scalar);
    static abstract A Antipode(A value);
}

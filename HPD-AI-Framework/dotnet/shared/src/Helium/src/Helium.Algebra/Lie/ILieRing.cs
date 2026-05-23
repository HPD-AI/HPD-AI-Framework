using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// A Lie ring: an additive commutative group with an exact bracket operation.
/// </summary>
public interface ILieRing<L> : IAddCommGroup<L>
    where L : ILieRing<L>
{
    static abstract L Bracket(L left, L right);
}

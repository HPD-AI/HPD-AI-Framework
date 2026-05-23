namespace Helium.Primitives;

/// <summary>
/// A group with a single multiplication-like operation, identity, inverse, and decidable equality.
/// </summary>
public interface IGroup<G> : IDecidableEq<G>
    where G : IGroup<G>
{
    static abstract G Identity { get; }
    static abstract G Multiply(G left, G right);
    static abstract G Invert(G value);
}

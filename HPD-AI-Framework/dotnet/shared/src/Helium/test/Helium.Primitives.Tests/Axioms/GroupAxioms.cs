using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

public static class GroupAxioms
{
    public static void VerifyIdentity<G>(G a) where G : IGroup<G>
    {
        Assert.Equal(a, G.Multiply(G.Identity, a));
        Assert.Equal(a, G.Multiply(a, G.Identity));
    }

    public static void VerifyAssociativity<G>(G a, G b, G c) where G : IGroup<G>
    {
        Assert.Equal(G.Multiply(G.Multiply(a, b), c), G.Multiply(a, G.Multiply(b, c)));
    }

    public static void VerifyInverse<G>(G a) where G : IGroup<G>
    {
        Assert.Equal(G.Identity, G.Multiply(a, G.Invert(a)));
        Assert.Equal(G.Identity, G.Multiply(G.Invert(a), a));
    }

    public static void VerifyAll<G>(G a, G b, G c) where G : IGroup<G>
    {
        VerifyIdentity(a);
        VerifyAssociativity(a, b, c);
        VerifyInverse(a);
    }
}
